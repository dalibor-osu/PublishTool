using System.Runtime.Versioning;
using PublishTool;
using PublishTool.Attributes;
using PublishTool.Commands;
using PublishTool.Configuration;
using PublishTool.Deployment;
using PublishTool.GitHub;
using PublishTool.Helpers;
using PublishToolCli.Commands.Options;
using PublishToolCli.Output;

namespace PublishToolCli.Commands;

[Command("deploy", Description = "Deploy a package created by publish to the targets of its prefix on this server (Windows only). "
    + "Without --yes it is a dry run")]
public class DeployCommand(DeployCommandOptions options) : ICommand<DeployCommandOptions>
{
    public DeployCommandOptions Options { get; } = options;

    public Task<int> ExecuteAsync(CancellationToken ct)
    {
        var output = new CliOutput(Options.Json, Options.Verbose);
        var result = new DeployCliResult { PackageDirectory = Options.WorkingDirectory, DryRun = !Options.Executes };
        return output.RunAsync(result, CliJsonContext.Default.DeployCliResult, () => RunAsync(output, result, ct), ct);
    }

    private Task<int> RunAsync(CliOutput output, DeployCliResult result, CancellationToken ct) =>
        OperatingSystem.IsWindows()
            ? RunOnWindowsAsync(output, result, ct)
            : Task.FromResult(output.Fail(result, ExitCodes.Failed, "deploy is supported on Windows only"));

    [SupportedOSPlatform("windows")]
    private async Task<int> RunOnWindowsAsync(CliOutput output, DeployCliResult result, CancellationToken ct)
    {
        if (Options.Executes && !WindowsAccount.IsAdministrator())
        {
            return output.Fail(result, ExitCodes.Failed,
                "deploy --yes stops IIS and services, run it from an elevated terminal (Run as administrator), or leave out --yes for a dry run");
        }

        var package = DeployPackage.Load(Options.WorkingDirectory);
        result.Package = package.Manifest.Name;
        result.Prefix = package.Manifest.Prefix;
        if (Options.Executes && Directory.Exists(package.BackupDirectory))
        {
            throw new DeployException(
                $"{package.BackupDirectory} already exists, so this package was deployed before or a deploy was interrupted. Run "
                + $"'{SelfUpdate.Cli.AppName} rollback --yes -dir \"{package.Directory}\"' first; deploying again would overwrite the backup.");
        }

        output.Line($"Verifying {package.FileCount} files of {package.Manifest.Name}...");
        var verification = package.Verify(_ => { }, ct);
        result.ExtraFiles = [.. verification.ExtraFiles];
        if (verification.Errors.Count > 0)
        {
            foreach (string error in verification.Errors)
            {
                output.Fail(result, ExitCodes.Failed, error);
            }

            return output.Fail(result, ExitCodes.Failed,
                $"The package does not match its manifest ({verification.Errors.Count} problem(s)), was it copied completely?");
        }

        var store = ConfigStore.Server;
        var config = store.Load();
        var hosts = new WindowsHostController();
        var resolution = DeployTargets.Resolve(config, package.Manifest.Prefix, [.. package.Manifest.Projects.Select(p => p.Name)], Options.Targets);
        if (resolution.Missing.Count > 0)
        {
            result.MissingTargets = [.. resolution.Missing];
            string first = resolution.Missing[0];
            return output.Fail(result, ExitCodes.InvalidArguments,
                $"No target directory is set for {string.Join(", ", resolution.Missing)} in packages of '{package.Manifest.Prefix}' in {store.Path}. "
                + $"Pass --target {first}=<absolute path> (plus --service {first}=<name> for a Windows service) for this run, or store it with "
                + $"'{SelfUpdate.Cli.AppName} config --prefix {package.Manifest.Prefix} --target {first}=<absolute path>'");
        }

        var plan = DeployPlan.Create(package, resolution.Targets, config, hosts, BuildInfo.Version);
        WritePlan(output, result, plan, verification, Options.Accept);
        if (plan.Errors.Count > 0)
        {
            foreach (string error in plan.Errors)
            {
                output.Fail(result, ExitCodes.Failed, error);
            }

            return ExitCodes.Failed;
        }

        return await DeployAsync(output, result, plan, config, store, hosts, ct);
    }

    [SupportedOSPlatform("windows")]
    private async Task<int> DeployAsync(
        CliOutput output,
        DeployCliResult result,
        DeployPlan plan,
        ServerConfig config,
        ConfigStore<ServerConfig> store,
        IHostController hosts,
        CancellationToken ct)
    {
        if (!Options.Executes)
        {
            output.Line(Options.Yes ? "Dry run, nothing was changed." : "Dry run, nothing was changed. Run again with --yes to deploy.");
            return ExitCodes.Success;
        }

        if (plan.IsEmpty)
        {
            output.Line("The package contains no changes, nothing to deploy.");
            return ExitCodes.Success;
        }

        var notAccepted = WarningCodes.NotAccepted(plan.Warnings, Options.Accept);
        if (notAccepted.Count > 0)
        {
            string codes = string.Join(",", notAccepted.Select(w => w.Code).Distinct());
            return output.Fail(result, ExitCodes.WarningsNotAccepted,
                $"Stopped before changing anything: {notAccepted.Count} warning(s) were not accepted. Review them and add --accept {codes} to deploy anyway");
        }

        var steps = new LineSteps(output);
        var runner = new DeployRunner(plan, config, store, hosts, BuildInfo.Version, SelfUpdate.Cli.AppName, steps);
        var outcome = await runner.RunAsync(ct);
        result.Steps = steps.ToResult();
        switch (outcome)
        {
            case DeployOutcome.Deployed:
                result.Changed = true;
                output.Line($"Deployed {plan.Package.Manifest.Name} to {plan.Targets.Count} target(s).");
                return ExitCodes.Success;
            case DeployOutcome.RestoreFailed:
                result.Changed = true;
                return output.Fail(result, ExitCodes.RestoreFailed,
                    $"The deploy failed and its automatic restore failed too; fix the cause and run '{SelfUpdate.Cli.AppName} rollback --yes -dir \"{plan.Package.Directory}\"'");
            default:
                return ct.IsCancellationRequested
                    ? ExitCodes.Cancelled
                    : output.Fail(result, ExitCodes.Failed, "The deploy failed, nothing was left changed (see the failed step above)");
        }
    }

    private static void WritePlan(
        CliOutput output, DeployCliResult result, DeployPlan plan, PackageVerification verification, IReadOnlyCollection<string> accepted)
    {
        var manifest = plan.Package.Manifest;
        output.Line($"Package {manifest.Name} (prefix {manifest.Prefix}), created {manifest.Created:yyyy-MM-dd HH:mm:ss} by PublishTool "
            + $"{manifest.ToolVersion}, deployed by {SelfUpdate.Cli.AppName} {BuildInfo.Version}");
        foreach (var target in plan.Targets)
        {
            string host = target.ServiceName is null ? "IIS" : $"service {target.ServiceName}";
            string basedOn = target.Project.IsFullPublish ? "full publish" : target.Project.Base ?? "unknown";
            output.Line($"  {target.Project.Name} -> {target.TargetPath} ({host}): {target.Create.Count} new, {target.Overwrite.Count} overwritten, "
                + $"{target.Delete.Count} deleted; deployed now: {target.Current?.Package ?? "-"}; based on: {basedOn}");
            result.Targets.Add(new CliDeployTarget(target.Project.Name, target.TargetPath, target.ServiceName, target.Create.Count,
                target.Overwrite.Count, target.Delete.Count, target.Current?.Package, target.Project.Base, target.Project.IsFullPublish));
        }

        if (verification.ExtraFiles.Count > 0)
        {
            output.Line($"{verification.ExtraFiles.Count} file(s) in the package are not in the manifest and are not deployed");
        }

        result.Warnings = CliOutput.Warnings(plan.Warnings, accepted);
        foreach (var warning in result.Warnings)
        {
            output.Warning(warning);
        }
    }
}
