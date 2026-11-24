using PublishTool.Configuration;
using PublishTool.Helpers;
using PublishTool.Progress;

namespace PublishTool.Deployment;

public sealed class RollbackRunner
{
    private readonly string _packageDirectory;
    private readonly DeployJournal _journal;
    private readonly ServerConfig _config;
    private readonly ConfigStore<ServerConfig> _store;
    private readonly IHostController _hosts;
    private readonly IStep _stopRow;
    private readonly IStep _restoreRow;
    private readonly IStep _startRow;
    private readonly IStep _recordRow;
    private readonly IStep _cleanupRow;

    public RollbackRunner(
        string packageDirectory,
        DeployJournal journal,
        ServerConfig config,
        ConfigStore<ServerConfig> store,
        IHostController hosts,
        IStepList steps)
    {
        _packageDirectory = packageDirectory;
        _journal = journal;
        _config = config;
        _store = store;
        _hosts = hosts;
        _stopRow = steps.Add("Stopping IIS and the services of the targets");
        _restoreRow = steps.Add($"Restoring the backup from {DeployPackage.BackupDirectoryOf(packageDirectory)}");
        _startRow = steps.Add("Starting IIS and the services again");
        _recordRow = steps.Add($"Resetting the deploy records in {store.Path}");
        _cleanupRow = steps.Add("Removing the backup");
    }

    public static IReadOnlyList<DeployWarning> CheckChain(DeployJournal journal, ServerConfig config)
    {
        var warnings = new List<DeployWarning>();
        if (journal.State is JournalState.Applying or JournalState.RestoreFailed)
        {
            return warnings;
        }

        foreach (var target in journal.Targets)
        {
            string? current = config.RecordFor(target.Path)?.Package;
            if (!string.Equals(current, journal.Package, StringComparison.OrdinalIgnoreCase))
            {
                warnings.Add(new DeployWarning(WarningCodes.NewerDeploy,
                    $"{target.Project}: {target.Path} is at {current ?? "no recorded deploy"}, not at {journal.Package}. "
                    + "Rolling back restores only the files this package replaced, on top of the newer files."));
            }
        }

        return warnings;
    }

    public Task<bool> RunAsync() => Task.Run(ExecuteAsync, CancellationToken.None);

    private async Task<bool> ExecuteAsync()
    {
        using var critical = CriticalSection.Enter();
        var running = HostSet.Needed(_journal.Targets).Running(_hosts);
        var restart = _journal.State is JournalState.Applying or JournalState.RestoreFailed
            ? running.Union(new HostSet(_journal.RestartIis, _journal.RestartServices))
            : running;
        _startRow.Name = $"Starting {restart.Describe()} again";

        bool restored = await StopAsync(running) && Restore();
        bool started = await StartAsync(restart);
        if (!restored)
        {
            _recordRow.Finish(StepState.Skipped, "the restore did not finish");
            _cleanupRow.Finish(StepState.Skipped, "the backup is kept");
            return false;
        }

        bool recorded = ResetRecords();
        if (recorded)
        {
            _cleanupRow.Start();
            DeployPreparer.TryDelete(DeployPackage.BackupDirectoryOf(_packageDirectory));
            _cleanupRow.Finish(StepState.Succeeded);
        }
        else
        {
            _cleanupRow.Finish(StepState.Skipped, "the backup is kept");
        }

        return started && recorded;
    }

    private async Task<bool> StopAsync(HostSet running)
    {
        _stopRow.Name = $"Stopping {running.Describe()}";
        _stopRow.Start();
        try
        {
            await running.StopAsync(_hosts, _stopRow.AppendLine, CancellationToken.None);
            _stopRow.Finish(StepState.Succeeded);
            return true;
        }
        catch (Exception e)
        {
            Logger.LogError($"Stopping the hosts for the rollback failed:\n{e}");
            _stopRow.Finish(StepState.Failed, e.Message);
            _restoreRow.Finish(StepState.Skipped, "the hosts could not be stopped");
            return false;
        }
    }

    private bool Restore()
    {
        _restoreRow.Start();
        var errors = DeployExecutor.Restore(DeployPackage.BackupDirectoryOf(_packageDirectory), _journal, _restoreRow.AppendLine);
        if (errors.Count == 0)
        {
            _restoreRow.Finish(StepState.Succeeded);
            return true;
        }

        foreach (string error in errors)
        {
            _restoreRow.AppendLine($"error: {error}");
        }

        _restoreRow.Finish(StepState.Failed, $"{errors.Count} problem(s), the backup is kept; fix the cause and run rollback again");
        return false;
    }

    private async Task<bool> StartAsync(HostSet restart)
    {
        _startRow.Start();
        try
        {
            await restart.StartAsync(_hosts, _startRow.AppendLine, CancellationToken.None);
            _startRow.Finish(StepState.Succeeded);
            return true;
        }
        catch (Exception e)
        {
            Logger.LogError($"Starting the hosts after the rollback failed:\n{e}");
            _startRow.Finish(StepState.Failed, e.Message);
            return false;
        }
    }

    private bool ResetRecords()
    {
        _recordRow.Start();
        try
        {
            foreach (var target in _journal.Targets)
            {
                _config.SetRecord(target.Path, target.Previous);
            }

            _store.Save(_config);
            _recordRow.Finish(StepState.Succeeded);
            return true;
        }
        catch (Exception e)
        {
            Logger.LogError($"Resetting the deploy records failed:\n{e}");
            _recordRow.Finish(StepState.Failed, e.Message);
            return false;
        }
    }
}
