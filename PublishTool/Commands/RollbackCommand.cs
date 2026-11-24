using System.Runtime.Versioning;
using PublishTool.Attributes;
using PublishTool.Commands.Options;
using PublishTool.Configuration;
using PublishTool.Console;
using PublishTool.Deployment;
using PublishTool.Helpers;
using Spectre.Console;

namespace PublishTool.Commands;

[Command("rollback", Description = "Restore the files a deploy of a package replaced or deleted (Windows only, needs administrator rights)")]
public class RollbackCommand(RollbackCommandOptions options) : ICommand<RollbackCommandOptions>, IAlternateScreenCommand
{
    public RollbackCommandOptions Options { get; } = options;

    public bool UsesAlternateScreen => true;

    public async Task<int> ExecuteAsync(CancellationToken ct)
    {
        if (!OperatingSystem.IsWindows())
        {
            AnsiConsole.MarkupLine("[red]rollback is supported on Windows only.[/]");
            return 1;
        }

        try
        {
            return await RunAsync(ct);
        }
        catch (DeployException e)
        {
            AnsiConsole.MarkupLineInterpolated($"[red]{e.Message}[/]");
            Logger.LogError(e.ToString());
            return 1;
        }
        catch (OperationCanceledException)
        {
            AnsiConsole.MarkupLine("[yellow]Cancelled, nothing was changed.[/]");
            return 1;
        }
    }

    [SupportedOSPlatform("windows")]
    private async Task<int> RunAsync(CancellationToken ct)
    {
        if (!WindowsAccount.IsAdministrator())
        {
            AnsiConsole.MarkupLine("[red]rollback stops IIS and services, run it from an elevated terminal (Run as administrator).[/]");
            return 1;
        }

        string directory = Options.WorkingDirectory;
        string journalPath = DeployPackage.JournalPathOf(directory);
        if (!File.Exists(journalPath))
        {
            throw new DeployException($"{journalPath} was not found, there is no deploy of this package to roll back.");
        }

        var journal = PackageJson.ReadJournal(journalPath);
        if (journal.State == JournalState.BackingUp)
        {
            DeployPreparer.TryDelete(DeployPackage.BackupDirectoryOf(directory));
            AnsiConsole.MarkupLine(
                "[green]The deploy stopped while it was backing up, nothing on the server was changed. The incomplete backup was removed.[/]");
            return 0;
        }

        var store = ConfigStore.Server;
        var config = store.Load();
        var warnings = RollbackRunner.CheckChain(journal, config);
        WriteSummary(journal, warnings);
        if (!await DeploySummary.AcceptAsync(warnings, $"Roll back {journal.Package}", ct))
        {
            return 1;
        }

        AnsiConsole.Clear();

        var display = new JobDisplay();
        var runner = new RollbackRunner(directory, journal, config, store, new WindowsHostController(), display);
        var work = runner.RunAsync();
        await JobDisplayRunner.RunAsync(display, work, live: true);
        return await work ? 0 : 1;
    }

    private static void WriteSummary(DeployJournal journal, IReadOnlyList<DeployWarning> warnings)
    {
        if (journal.State == JournalState.Applying)
        {
            AnsiConsole.MarkupLineInterpolated(
                $"Rolling back the [yellow]interrupted[/] deploy of [green]{journal.Package}[/] (started {journal.Started:yyyy-MM-dd HH:mm:ss})");
        }
        else if (journal.State == JournalState.RestoreFailed)
        {
            AnsiConsole.MarkupLineInterpolated(
                $"Finishing the restore of the [yellow]failed[/] deploy of [green]{journal.Package}[/] (started {journal.Started:yyyy-MM-dd HH:mm:ss}); its automatic restore did not complete");
        }
        else
        {
            AnsiConsole.MarkupLineInterpolated(
                $"Rolling back the deploy of [green]{journal.Package}[/] (started {journal.Started:yyyy-MM-dd HH:mm:ss})");
        }

        var table = new Table().AddColumns("Project", "Target", "Host", "Restore", "Remove", "Record after");
        foreach (var target in journal.Targets)
        {
            table.AddRow(
                Markup.Escape(target.Project),
                Markup.Escape(target.Path),
                Markup.Escape(string.IsNullOrWhiteSpace(target.ServiceName) ? "IIS" : $"service {target.ServiceName}"),
                target.BackedUp.Count.ToString(),
                target.Created.Count.ToString(),
                Markup.Escape(target.Previous?.Package ?? "-"));
        }

        AnsiConsole.Write(table);
        DeploySummary.WriteIssues(warnings, []);
    }
}
