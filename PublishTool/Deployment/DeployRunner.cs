using PublishTool.Configuration;
using PublishTool.Console;
using PublishTool.Helpers;

namespace PublishTool.Deployment;

internal sealed class DeployRunner
{
    private readonly DeployPlan _plan;
    private readonly ServerConfig _config;
    private readonly ConfigStore<ServerConfig> _store;
    private readonly IHostController _hosts;
    private readonly string _toolVersion;
    private readonly JobDisplay _display = new();
    private readonly JobRow _backupRow;
    private readonly JobRow _stopRow;
    private readonly JobRow _applyRow;
    private readonly JobRow _startRow;
    private readonly JobRow _recordRow;
    private readonly JobRow _restoreRow;
    private readonly JobRow _restartRow;
    private HostSet _restart = new(false, []);
    private bool _applyStarted;
    private bool _recorded;

    public DeployRunner(DeployPlan plan, ServerConfig config, ConfigStore<ServerConfig> store, IHostController hosts, string toolVersion)
    {
        _plan = plan;
        _config = config;
        _store = store;
        _hosts = hosts;
        _toolVersion = toolVersion;
        _backupRow = _display.Add($"Backing up the files that will be overwritten or deleted into {plan.Package.BackupDirectory}");
        _stopRow = _display.Add("Stopping IIS and the services of the targets");
        _applyRow = _display.Add(
            $"Copying {plan.Targets.Sum(t => t.Project.Files.Count)} files and deleting {plan.Targets.Sum(t => t.Delete.Count)} files");
        _startRow = _display.Add("Starting IIS and the services again");
        _recordRow = _display.Add($"Recording the deploy in {store.Path}");
        _restoreRow = _display.Add("Restoring the backup (only after a failure)");
        _restartRow = _display.Add("Starting IIS and the services after the restore (only after a failure)");
    }

    private IEnumerable<JobRow> DeployRows => [_stopRow, _applyRow, _startRow, _recordRow];

    public async Task<bool> RunAsync(bool live, CancellationToken ct)
    {
        var work = Task.Run(() => ExecuteAsync(ct), CancellationToken.None);
        await JobDisplayRunner.RunAsync(_display, work, live);
        return await work;
    }

    private async Task<bool> ExecuteAsync(CancellationToken ct)
    {
        var journal = DeployExecutor.CreateJournal(_plan, _toolVersion);
        if (!Backup(journal, ct))
        {
            return false;
        }

        using var critical = CriticalSection.Enter();
        try
        {
            await StopAsync(journal, ct);
            Apply(ct);
            await StartAsync(ct);
            Record(journal);
        }
        catch (Exception e)
        {
            FinishAfterFailure(e);
            await RollbackAsync(journal);
            return false;
        }

        _restoreRow.Finish(JobState.Skipped, "not needed");
        _restartRow.Finish(JobState.Skipped, "not needed");
        return true;
    }

    private bool Backup(DeployJournal journal, CancellationToken ct)
    {
        _backupRow.Start();
        bool existedBefore = Directory.Exists(_plan.Package.BackupDirectory);
        try
        {
            int count = DeployExecutor.Backup(_plan, journal, _backupRow.AppendLine, ct);
            _backupRow.Finish(JobState.Succeeded, $"{count} files");
            return true;
        }
        catch (Exception e)
        {
            bool cancelled = e is OperationCanceledException;
            if (!cancelled)
            {
                Logger.LogError($"Backing up for {_plan.Package.Manifest.Name} failed:\n{e}");
            }

            if (!existedBefore)
            {
                DeployPreparer.TryDelete(_plan.Package.BackupDirectory);
            }

            _backupRow.Finish(cancelled ? JobState.Cancelled : JobState.Failed, $"{(cancelled ? string.Empty : e.Message + ", ")}nothing was changed");
            foreach (var row in DeployRows.Append(_restoreRow).Append(_restartRow))
            {
                row.Finish(cancelled ? JobState.Cancelled : JobState.Skipped, "the backup did not finish");
            }

            return false;
        }
    }

    private async Task StopAsync(DeployJournal journal, CancellationToken ct)
    {
        _restart = HostSet.Needed(journal.Targets).Running(_hosts);
        journal.RestartIis = _restart.Iis;
        journal.RestartServices = [.. _restart.Services];
        journal.State = JournalState.Applying;
        PackageJson.WriteJournal(_plan.Package.JournalPath, journal);

        _stopRow.Name = $"Stopping {_restart.Describe()}";
        _startRow.Name = $"Starting {_restart.Describe()} again";
        _stopRow.Start();
        await _restart.StopAsync(_hosts, _stopRow.AppendLine, ct);
        _stopRow.Finish(JobState.Succeeded);
    }

    private void Apply(CancellationToken ct)
    {
        _applyStarted = true;
        _applyRow.Start();
        var (copied, deleted) = DeployExecutor.Apply(_plan, _applyRow.AppendLine, ct);
        _applyRow.Finish(JobState.Succeeded, $"{copied} copied, {deleted} deleted");
    }

    private async Task StartAsync(CancellationToken ct)
    {
        _startRow.Start();
        await _restart.StartAsync(_hosts, _startRow.AppendLine, ct);
        _startRow.Finish(JobState.Succeeded);
    }

    private void Record(DeployJournal journal)
    {
        _recordRow.Start();
        var time = DateTimeOffset.Now;
        _recorded = true;
        foreach (var target in _plan.Targets)
        {
            _config.SetRecord(target.TargetPath, new DeployRecord { Package = _plan.Package.Manifest.Name, Time = time });
        }

        _store.Save(_config);
        journal.State = JournalState.Deployed;
        PackageJson.WriteJournal(_plan.Package.JournalPath, journal);
        _recordRow.Finish(JobState.Succeeded, $"{_plan.Targets.Count} target(s) are at {_plan.Package.Manifest.Name}");
    }

    private void FinishAfterFailure(Exception e)
    {
        bool cancelled = e is OperationCanceledException;
        if (!cancelled)
        {
            Logger.LogError($"Deploying {_plan.Package.Manifest.Name} failed:\n{e}");
        }

        var failedRow = DeployRows.FirstOrDefault(r => r.State == JobState.Running) ?? DeployRows.FirstOrDefault(r => !r.IsFinished);
        failedRow?.Finish(cancelled ? JobState.Cancelled : JobState.Failed, cancelled ? "rolling back" : e.Message);
        foreach (var row in DeployRows.Where(r => !r.IsFinished))
        {
            row.Finish(JobState.Skipped, "rolled back");
        }
    }

    private async Task RollbackAsync(DeployJournal journal)
    {
        if (_applyStarted)
        {
            await RestoreAsync(journal);
        }
        else
        {
            DeployPreparer.TryDelete(_plan.Package.BackupDirectory);
            _restoreRow.Finish(JobState.Skipped, "no file was changed yet, the backup was removed");
        }

        _restartRow.Name = $"Starting {_restart.Describe()} after the restore";
        _restartRow.Start();
        try
        {
            await _restart.StartAsync(_hosts, _restartRow.AppendLine, CancellationToken.None);
            _restartRow.Finish(JobState.Succeeded);
        }
        catch (Exception e)
        {
            Logger.LogError($"Starting the hosts after the restore failed:\n{e}");
            _restartRow.Finish(JobState.Failed, e.Message);
        }
    }

    private async Task RestoreAsync(DeployJournal journal)
    {
        _restoreRow.Name = $"Restoring the backup from {_plan.Package.BackupDirectory}";
        _restoreRow.Start();
        try
        {
            await _restart.StopAsync(_hosts, _restoreRow.AppendLine, CancellationToken.None);
        }
        catch (Exception e)
        {
            Logger.LogError($"Stopping the hosts before the restore failed:\n{e}");
            _restoreRow.AppendLine($"error: stopping the hosts before the restore failed: {e.Message}");
        }

        var errors = DeployExecutor.Restore(_plan.Package.BackupDirectory, journal, _restoreRow.AppendLine).ToList();
        if (_recorded)
        {
            ResetRecords(journal, errors);
        }

        if (errors.Count == 0)
        {
            DeployPreparer.TryDelete(_plan.Package.BackupDirectory);
            _restoreRow.Finish(JobState.Succeeded, "the targets are back to their previous files, the backup was removed");
            return;
        }

        foreach (string error in errors)
        {
            _restoreRow.AppendLine($"error: {error}");
        }

        try
        {
            journal.State = JournalState.RestoreFailed;
            PackageJson.WriteJournal(_plan.Package.JournalPath, journal);
        }
        catch (Exception e)
        {
            Logger.LogError($"Marking the journal as restore failed failed:\n{e}");
        }

        _restoreRow.Finish(JobState.Failed,
            $"{errors.Count} problem(s), the backup is kept; fix the cause and run 'PublishTool rollback -dir {_plan.Package.Directory}'");
    }

    private void ResetRecords(DeployJournal journal, List<string> errors)
    {
        try
        {
            foreach (var target in journal.Targets)
            {
                _config.SetRecord(target.Path, target.Previous);
            }

            _store.Save(_config);
        }
        catch (Exception e)
        {
            Logger.LogError($"Resetting the deploy records failed:\n{e}");
            errors.Add($"resetting the deploy records in {_store.Path}: {e.Message}");
        }
    }
}
