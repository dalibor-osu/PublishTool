using PublishTool.Configuration;
using PublishTool.Helpers;
using PublishTool.Progress;

namespace PublishTool.Deployment;

public enum DeployOutcome
{
    Deployed,
    Failed,
    RestoreFailed
}

public sealed class DeployRunner
{
    private readonly DeployPlan _plan;
    private readonly ServerConfig _config;
    private readonly ConfigStore<ServerConfig> _store;
    private readonly IHostController _hosts;
    private readonly string _toolVersion;
    private readonly string _executableName;
    private readonly IStep _backupRow;
    private readonly IStep _stopRow;
    private readonly IStep _applyRow;
    private readonly IStep _startRow;
    private readonly IStep _recordRow;
    private readonly IStep _restoreRow;
    private readonly IStep _restartRow;
    private HostSet _restart = new(false, []);
    private bool _applyStarted;
    private bool _recorded;
    private bool _restoreFailed;

    public DeployRunner(
        DeployPlan plan,
        ServerConfig config,
        ConfigStore<ServerConfig> store,
        IHostController hosts,
        string toolVersion,
        string executableName,
        IStepList steps)
    {
        _plan = plan;
        _config = config;
        _store = store;
        _hosts = hosts;
        _toolVersion = toolVersion;
        _executableName = executableName;
        _backupRow = steps.Add($"Backing up the files that will be overwritten or deleted into {plan.Package.BackupDirectory}");
        _stopRow = steps.Add("Stopping IIS and the services of the targets");
        _applyRow = steps.Add(
            $"Copying {plan.Targets.Sum(t => t.Project.Files.Count)} files and deleting {plan.Targets.Sum(t => t.Delete.Count + t.Stray.Count)} files");
        _startRow = steps.Add("Starting IIS and the services again");
        _recordRow = steps.Add($"Recording the deploy in {store.Path}");
        _restoreRow = steps.Add("Restoring the backup (only after a failure)");
        _restartRow = steps.Add("Starting IIS and the services after the restore (only after a failure)");
    }

    private IEnumerable<IStep> DeployRows => [_stopRow, _applyRow, _startRow, _recordRow];

    public Task<DeployOutcome> RunAsync(CancellationToken ct) => Task.Run(() => ExecuteAsync(ct), CancellationToken.None);

    private async Task<DeployOutcome> ExecuteAsync(CancellationToken ct)
    {
        var journal = DeployExecutor.CreateJournal(_plan, _toolVersion);
        if (!Backup(journal, ct))
        {
            return DeployOutcome.Failed;
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
            return _restoreFailed ? DeployOutcome.RestoreFailed : DeployOutcome.Failed;
        }

        _restoreRow.Finish(StepState.Skipped, "not needed");
        _restartRow.Finish(StepState.Skipped, "not needed");
        return DeployOutcome.Deployed;
    }

    private bool Backup(DeployJournal journal, CancellationToken ct)
    {
        _backupRow.Start();
        bool existedBefore = Directory.Exists(_plan.Package.BackupDirectory);
        try
        {
            int count = DeployExecutor.Backup(_plan, journal, _backupRow.AppendLine, ct);
            _backupRow.Finish(StepState.Succeeded, $"{count} files");
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

            _backupRow.Finish(cancelled ? StepState.Cancelled : StepState.Failed, $"{(cancelled ? string.Empty : e.Message + ", ")}nothing was changed");
            foreach (var row in DeployRows.Append(_restoreRow).Append(_restartRow))
            {
                row.Finish(cancelled ? StepState.Cancelled : StepState.Skipped, "the backup did not finish");
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
        _stopRow.Finish(StepState.Succeeded);
    }

    private void Apply(CancellationToken ct)
    {
        _applyStarted = true;
        _applyRow.Start();
        var (copied, deleted) = DeployExecutor.Apply(_plan, _applyRow.AppendLine, ct);
        _applyRow.Finish(StepState.Succeeded, $"{copied} copied, {deleted} deleted");
    }

    private async Task StartAsync(CancellationToken ct)
    {
        _startRow.Start();
        await _restart.StartAsync(_hosts, _startRow.AppendLine, ct);
        _startRow.Finish(StepState.Succeeded);
    }

    private void Record(DeployJournal journal)
    {
        _recordRow.Start();
        var time = DateTimeOffset.Now;
        _recorded = true;
        var deployed = _plan.Targets.Where(t => !t.IsCleanOnly).ToList();
        foreach (var target in deployed)
        {
            _config.SetRecord(target.TargetPath, new DeployRecord { Package = _plan.Package.Manifest.Name, Time = time });
        }

        _store.Save(_config);
        journal.State = JournalState.Deployed;
        PackageJson.WriteJournal(_plan.Package.JournalPath, journal);
        _recordRow.Finish(StepState.Succeeded, $"{deployed.Count} target(s) are at {_plan.Package.Manifest.Name}");
    }

    private void FinishAfterFailure(Exception e)
    {
        bool cancelled = e is OperationCanceledException;
        if (!cancelled)
        {
            Logger.LogError($"Deploying {_plan.Package.Manifest.Name} failed:\n{e}");
        }

        var failedRow = DeployRows.FirstOrDefault(r => r.State == StepState.Running) ?? DeployRows.FirstOrDefault(r => !r.IsFinished);
        failedRow?.Finish(cancelled ? StepState.Cancelled : StepState.Failed, cancelled ? "rolling back" : e.Message);
        foreach (var row in DeployRows.Where(r => !r.IsFinished))
        {
            row.Finish(StepState.Skipped, "rolled back");
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
            _restoreRow.Finish(StepState.Skipped, "no file was changed yet, the backup was removed");
        }

        _restartRow.Name = $"Starting {_restart.Describe()} after the restore";
        _restartRow.Start();
        try
        {
            await _restart.StartAsync(_hosts, _restartRow.AppendLine, CancellationToken.None);
            _restartRow.Finish(StepState.Succeeded);
        }
        catch (Exception e)
        {
            Logger.LogError($"Starting the hosts after the restore failed:\n{e}");
            _restartRow.Finish(StepState.Failed, e.Message);
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
            _restoreRow.Finish(StepState.Succeeded, "the targets are back to their previous files, the backup was removed");
            return;
        }

        _restoreFailed = true;
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

        _restoreRow.Finish(StepState.Failed,
            $"{errors.Count} problem(s), the backup is kept; fix the cause and run '{_executableName} rollback -dir {_plan.Package.Directory}'");
    }

    private void ResetRecords(DeployJournal journal, List<string> errors)
    {
        try
        {
            foreach (var target in journal.Targets.Where(t => !t.CleanOnly))
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
