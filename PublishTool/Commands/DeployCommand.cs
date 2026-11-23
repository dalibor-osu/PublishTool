using System.Runtime.Versioning;
using PublishTool.Attributes;
using PublishTool.Commands.Options;
using PublishTool.Configuration;
using PublishTool.Console;
using PublishTool.Deployment;
using PublishTool.Helpers;
using Spectre.Console;

namespace PublishTool.Commands;

[Command("deploy", Description = "Deploy a package created by publish to the targets mapped on this server (Windows only, needs "
    + "administrator rights)")]
public class DeployCommand(DeployCommandOptions options) : ICommand<DeployCommandOptions>
{
    public DeployCommandOptions Options { get; } = options;

    public bool UsesAlternateScreen => !Options.Yes;

    public async Task<int> ExecuteAsync(CancellationToken ct)
    {
        if (!OperatingSystem.IsWindows())
        {
            AnsiConsole.MarkupLine("[red]deploy is supported on Windows only.[/]");
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
        if (!Options.DryRun && !WindowsAccount.IsAdministrator())
        {
            AnsiConsole.MarkupLine("[red]deploy stops IIS and services, run it from an elevated terminal (Run as administrator).[/]");
            return 1;
        }

        var package = DeployPackage.Load(Options.WorkingDirectory);
        if (!Options.DryRun && Directory.Exists(package.BackupDirectory))
        {
            throw new DeployException(
                $"{package.BackupDirectory} already exists, so this package was deployed before or a deploy was interrupted. Run "
                + $"'PublishTool rollback -dir {package.Directory}' first; deploying again would overwrite the backup.");
        }

        AnsiConsole.MarkupLineInterpolated($"Verifying {package.FileCount} files of {package.Manifest.Name}...");
        var verification = package.Verify(_ => { }, ct);
        if (verification.Errors.Count > 0)
        {
            AnsiConsole.MarkupLineInterpolated(
                $"[red]The package does not match its manifest ({verification.Errors.Count} problem(s)), was it copied completely?[/]");
            DeploySummary.WriteList(verification.Errors);
            return 1;
        }

        var store = ConfigStore.Server;
        var config = store.Load();
        var hosts = new WindowsHostController();
        var mappings = ServerConfigHandler.ResolveTargets(
            store, config, package.Manifest.Prefix, [.. package.Manifest.Projects.Select(p => p.Name)], interactive: !Options.Yes,
            hosts.ServiceExists);
        var plan = DeployPlan.Create(package, mappings, config, hosts, BuildInfo.Version);
        DeploySummary.WritePlan(plan, verification, BuildInfo.Version);

        if (plan.Errors.Count > 0)
        {
            return 1;
        }

        if (Options.DryRun)
        {
            AnsiConsole.MarkupLine("[green]Dry run, nothing was changed.[/]");
            return 0;
        }

        if (plan.IsEmpty)
        {
            AnsiConsole.MarkupLine("[green]The package contains no changes, nothing to deploy.[/]");
            return 0;
        }

        if (!await DeploySummary.AcceptAsync(plan.Warnings, Options.Yes, $"Deploy {package.Manifest.Name}", ct))
        {
            return 1;
        }

        if (UsesAlternateScreen)
        {
            AnsiConsole.Clear();
        }

        var runner = new DeployRunner(plan, config, store, hosts, BuildInfo.Version);
        return await runner.RunAsync(UsesAlternateScreen, ct) ? 0 : 1;
    }
}
