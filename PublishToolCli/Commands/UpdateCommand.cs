using PublishTool;
using PublishTool.Attributes;
using PublishTool.Commands;
using PublishTool.Deployment;
using PublishTool.GitHub;
using PublishTool.Helpers;
using PublishToolCli.Commands.Options;
using PublishToolCli.Output;

namespace PublishToolCli.Commands;

[Command("update", Description = "Update PublishToolCli (only this executable) to the latest or a selected release")]
public class UpdateCommand(UpdateCommandOptions options) : ICommand<UpdateCommandOptions>
{
    public UpdateCommandOptions Options { get; } = options;

    public Task<int> ExecuteAsync(CancellationToken ct)
    {
        var output = new CliOutput(Options.Json, false);
        var result = new UpdateCliResult { FetchOnly = Options.Fetch };
        return output.RunAsync(result, CliJsonContext.Default.UpdateCliResult, () => RunAsync(output, result, ct), ct);
    }

    private async Task<int> RunAsync(CliOutput output, UpdateCliResult result, CancellationToken ct)
    {
        var client = new GitHubClient();
        bool exact = !string.IsNullOrWhiteSpace(Options.Version);
        var found = await SelfUpdate.FindAsync(client, Options.PreRelease, Options.Version, ct);
        ct.ThrowIfCancellationRequested();
        if (!found.IsSuccess)
        {
            return output.Fail(result, ExitCodes.Failed, $"Failed to find the release to install: {found.Error}");
        }

        var target = found.Value;
        var current = ReleaseVersion.Current;
        result.TargetVersion = target.ToString();
        output.Line($"Installed: {BuildInfo.Version}, target: {target}");
        if (!exact && current is not null && current > target)
        {
            result.Warnings.Add(new CliWarning(WarningCodes.Downgrade,
                $"{target} is older than the running {current}" + (current.IsPreRelease && !Options.PreRelease ? "; add --pre-release to stay on pre-releases" : string.Empty),
                Options.Accept.Contains(WarningCodes.Downgrade)));
            output.Warning(result.Warnings[0]);
        }

        if (Options.Fetch)
        {
            return ExitCodes.Success;
        }

        return await InstallAsync(output, result, client, target, ct);
    }

    private static async Task<int> InstallAsync(CliOutput output, UpdateCliResult result, GitHubClient client, ReleaseVersion target, CancellationToken ct)
    {
        var current = ReleaseVersion.Current;
        if (EnvironmentHelper.IsDevVersion || current is null)
        {
            return output.Fail(result, ExitCodes.Failed, "Dev builds cannot update themselves");
        }

        if (current.CompareTo(target) == 0)
        {
            output.Line("Already up to date.");
            return ExitCodes.Success;
        }

        if (result.Warnings.Exists(w => !w.Accepted))
        {
            return output.Fail(result, ExitCodes.WarningsNotAccepted,
                $"Stopped: installing {target} would downgrade {SelfUpdate.Cli.AppName}; add --accept {WarningCodes.Downgrade} to do it anyway");
        }

        var installed = await SelfUpdate.Cli.InstallAsync(client, target, output.Line, ct);
        if (!installed.IsSuccess)
        {
            ct.ThrowIfCancellationRequested();
            return output.Fail(result, ExitCodes.Failed, installed.Error);
        }

        result.Updated = true;
        output.Line($"Updated {SelfUpdate.Cli.AppName} to {target}.");
        return ExitCodes.Success;
    }
}
