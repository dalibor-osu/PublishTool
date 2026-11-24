using System.Runtime.Versioning;
using PublishTool;
using PublishTool.Attributes;
using PublishTool.Commands;
using PublishTool.Configuration;
using PublishTool.Deployment;
using PublishTool.Helpers;
using PublishToolCli.Commands.Options;
using PublishToolCli.Output;

namespace PublishToolCli.Commands;

[Command("rollback", Description = "Restore the files a deploy of a package replaced or deleted (Windows only). Without --yes it is a dry run")]
public class RollbackCommand(RollbackCommandOptions options) : ICommand<RollbackCommandOptions>
{
    public RollbackCommandOptions Options { get; } = options;

    public Task<int> ExecuteAsync(CancellationToken ct)
    {
        var output = new CliOutput(Options.Json, Options.Verbose);
        var result = new RollbackCliResult { PackageDirectory = Options.WorkingDirectory, DryRun = !Options.Yes };
        return output.RunAsync(result, CliJsonContext.Default.RollbackCliResult, () => RunAsync(output, result), ct);
    }

    private Task<int> RunAsync(CliOutput output, RollbackCliResult result) =>
        OperatingSystem.IsWindows()
            ? RunOnWindowsAsync(output, result)
            : Task.FromResult(output.Fail(result, ExitCodes.Failed, "rollback is supported on Windows only"));

    [SupportedOSPlatform("windows")]
    private async Task<int> RunOnWindowsAsync(CliOutput output, RollbackCliResult result)
    {
        if (Options.Yes && !WindowsAccount.IsAdministrator())
        {
            return output.Fail(result, ExitCodes.Failed,
                "rollback --yes stops IIS and services, run it from an elevated terminal (Run as administrator), or leave out --yes for a dry run");
        }

        string directory = Options.WorkingDirectory;
        string journalPath = DeployPackage.JournalPathOf(directory);
        if (!File.Exists(journalPath))
        {
            throw new DeployException($"{journalPath} was not found, there is no deploy of this package to roll back.");
        }

        var journal = PackageJson.ReadJournal(journalPath);
        result.Package = journal.Package;
        result.JournalState = journal.State.ToString();
        if (journal.State == JournalState.BackingUp)
        {
            return RemoveIncompleteBackup(output, result, directory);
        }

        var store = ConfigStore.Server;
        var config = store.Load();
        var warnings = RollbackRunner.CheckChain(journal, config);
        WriteSummary(output, result, journal, warnings);
        if (!Options.Yes)
        {
            output.Line("Dry run, nothing was changed. Run again with --yes to roll back.");
            return ExitCodes.Success;
        }

        var notAccepted = WarningCodes.NotAccepted(warnings, Options.Accept);
        if (notAccepted.Count > 0)
        {
            return output.Fail(result, ExitCodes.WarningsNotAccepted,
                $"Stopped before changing anything: {notAccepted.Count} warning(s) were not accepted. Review them and add --accept "
                + $"{WarningCodes.NewerDeploy} to roll back anyway");
        }

        var steps = new LineSteps(output);
        bool rolledBack = await new RollbackRunner(directory, journal, config, store, new WindowsHostController(), steps).RunAsync();
        result.Steps = steps.ToResult();
        result.Changed = true;
        if (!rolledBack)
        {
            return output.Fail(result, ExitCodes.Failed, "The rollback did not complete, see the failed step above; fix the cause and run it again");
        }

        output.Line($"Rolled back {journal.Package}.");
        return ExitCodes.Success;
    }

    private int RemoveIncompleteBackup(CliOutput output, RollbackCliResult result, string directory)
    {
        if (!Options.Yes)
        {
            output.Line("The deploy stopped while it was backing up, nothing on the server was changed. Run again with --yes to remove the "
                + "incomplete backup.");
            return ExitCodes.Success;
        }

        DeployPreparer.TryDelete(DeployPackage.BackupDirectoryOf(directory));
        result.Changed = true;
        output.Line("The deploy stopped while it was backing up, nothing on the server was changed. The incomplete backup was removed.");
        return ExitCodes.Success;
    }

    private void WriteSummary(CliOutput output, RollbackCliResult result, DeployJournal journal, IReadOnlyList<DeployWarning> warnings)
    {
        string kind = journal.State switch
        {
            JournalState.Applying => "the interrupted deploy",
            JournalState.RestoreFailed => "the failed deploy (its automatic restore did not complete)",
            _ => "the deploy"
        };
        output.Line($"Rolling back {kind} of {journal.Package} (started {journal.Started:yyyy-MM-dd HH:mm:ss})");
        foreach (var target in journal.Targets)
        {
            string host = string.IsNullOrWhiteSpace(target.ServiceName) ? "IIS" : $"service {target.ServiceName}";
            output.Line($"  {target.Project} -> {target.Path} ({host}): {target.BackedUp.Count} to restore, {target.Created.Count} to remove; "
                + $"record after: {target.Previous?.Package ?? "-"}");
            result.Targets.Add(new CliRollbackTarget(target.Project, target.Path, target.ServiceName, target.BackedUp.Count, target.Created.Count,
                target.Previous?.Package));
        }

        result.Warnings = CliOutput.Warnings(warnings, Options.Accept);
        foreach (var warning in result.Warnings)
        {
            output.Warning(warning);
        }
    }
}
