using PublishTool.Configuration;
using PublishTool.Console;
using PublishTool.Helpers;

namespace PublishTool.Deployment;

internal sealed class RollbackRunner
{
    private readonly string _packageDirectory;
    private readonly DeployJournal _journal;
    private readonly ServerConfig _config;
    private readonly ConfigStore<ServerConfig> _store;
    private readonly IHostController _hosts;
    private readonly JobDisplay _display = new();
    private readonly JobRow _stopRow;
    private readonly JobRow _restoreRow;
    private readonly JobRow _startRow;
    private readonly JobRow _recordRow;
    private readonly JobRow _cleanupRow;

    public RollbackRunner(string packageDirectory, DeployJournal journal, ServerConfig config, ConfigStore<ServerConfig> store, IHostController hosts)
    {
        _packageDirectory = packageDirectory;
        _journal = journal;
        _config = config;
        _store = store;
        _hosts = hosts;
        _stopRow = _display.Add("Stopping IIS and the services of the targets");
        _restoreRow = _display.Add($"Restoring the backup from {DeployPackage.BackupDirectoryOf(packageDirectory)}");
        _startRow = _display.Add("Starting IIS and the services again");
        _recordRow = _display.Add($"Resetting the deploy records in {store.Path}");
        _cleanupRow = _display.Add("Removing the backup");
    }

    public async Task<bool> RunAsync(bool live)
    {
        var work = Task.Run(ExecuteAsync, CancellationToken.None);
        await JobDisplayRunner.RunAsync(_display, work, live);
        return await work;
    }

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
            _recordRow.Finish(JobState.Skipped, "the restore did not finish");
            _cleanupRow.Finish(JobState.Skipped, "the backup is kept");
            return false;
        }

        bool recorded = ResetRecords();
        if (recorded)
        {
            _cleanupRow.Start();
            DeployPreparer.TryDelete(DeployPackage.BackupDirectoryOf(_packageDirectory));
            _cleanupRow.Finish(JobState.Succeeded);
        }
        else
        {
            _cleanupRow.Finish(JobState.Skipped, "the backup is kept");
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
            _stopRow.Finish(JobState.Succeeded);
            return true;
        }
        catch (Exception e)
        {
            Logger.LogError($"Stopping the hosts for the rollback failed:\n{e}");
            _stopRow.Finish(JobState.Failed, e.Message);
            _restoreRow.Finish(JobState.Skipped, "the hosts could not be stopped");
            return false;
        }
    }

    private bool Restore()
    {
        _restoreRow.Start();
        var errors = DeployExecutor.Restore(DeployPackage.BackupDirectoryOf(_packageDirectory), _journal, _restoreRow.AppendLine);
        if (errors.Count == 0)
        {
            _restoreRow.Finish(JobState.Succeeded);
            return true;
        }

        foreach (string error in errors)
        {
            _restoreRow.AppendLine($"error: {error}");
        }

        _restoreRow.Finish(JobState.Failed, $"{errors.Count} problem(s), the backup is kept; fix the cause and run rollback again");
        return false;
    }

    private async Task<bool> StartAsync(HostSet restart)
    {
        _startRow.Start();
        try
        {
            await restart.StartAsync(_hosts, _startRow.AppendLine, CancellationToken.None);
            _startRow.Finish(JobState.Succeeded);
            return true;
        }
        catch (Exception e)
        {
            Logger.LogError($"Starting the hosts after the rollback failed:\n{e}");
            _startRow.Finish(JobState.Failed, e.Message);
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
            _recordRow.Finish(JobState.Succeeded);
            return true;
        }
        catch (Exception e)
        {
            Logger.LogError($"Resetting the deploy records failed:\n{e}");
            _recordRow.Finish(JobState.Failed, e.Message);
            return false;
        }
    }
}
