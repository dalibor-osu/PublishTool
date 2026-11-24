using PublishTool.Attributes;
using PublishTool.Commands.Options;
using PublishTool.GitHub;
using PublishTool.Helpers;
using Spectre.Console;

namespace PublishTool.Commands;

[Command("update", Description = "Update PublishTool to the latest published or selected release")]
public class UpdateCommand(UpdateCommandOptions options) : ICommand<UpdateCommandOptions>
{
    public UpdateCommandOptions Options { get; } = options;

    public async Task<int> ExecuteAsync(CancellationToken ct)
    {
        var client = new GitHubClient();
        var currentVersion = ReleaseVersion.Current;
        bool exact = !string.IsNullOrWhiteSpace(Options.Version);

        if (!exact && currentVersion?.IsPreRelease == true && !Options.PreRelease)
        {
            Options.PreRelease = await AnsiConsole.ConfirmAsync(
                "You are currently running a pre-release version. Do you want to include pre-release versions?", cancellationToken: ct);
        }

        string channel = Channel(exact);
        AnsiConsole.WriteLine($"Fetching {(exact ? string.Empty : "latest ")}{channel}...");
        var result = await SelfUpdate.FindAsync(client, Options.PreRelease, Options.Version, ct);
        if (!result.IsSuccess)
        {
            AnsiConsole.MarkupLine($"[red]Failed to fetch {Markup.Escape(channel)}:[/] {Markup.Escape(result.Error)}");
            return 1;
        }

        var targetVersion = result.Value;

        if (Options.Fetch)
        {
            AnsiConsole.WriteLine($"Target {channel}: {targetVersion} (Current: {currentVersion?.ToString() ?? BuildInfo.Version})");
            if (!exact && currentVersion != null && currentVersion > targetVersion)
            {
                WriteDowngradeWarning(currentVersion, targetVersion);
            }

            return 0;
        }

        if (EnvironmentHelper.IsDevVersion || currentVersion == null)
        {
            AnsiConsole.WriteLine("Cannot update dev version");
            return 1;
        }

        int compareResult = currentVersion.CompareTo(targetVersion);
        if (compareResult == 0)
        {
            AnsiConsole.WriteLine("Current version is up to date");
            return 0;
        }

        if (compareResult > 0 && !exact)
        {
            WriteDowngradeWarning(currentVersion, targetVersion);
            if (!await AnsiConsole.ConfirmAsync($"Do you want to downgrade to {targetVersion}?", false, ct))
            {
                AnsiConsole.WriteLine("Update cancelled");
                return 0;
            }
        }

        var install = await SelfUpdate.Tui.InstallAsync(client, targetVersion, AnsiConsole.WriteLine, ct);
        if (!install.IsSuccess)
        {
            AnsiConsole.MarkupLine($"[red]{Markup.Escape(install.Error)}[/]");
            return 1;
        }

        AnsiConsole.MarkupLine($"[green]Updated to {targetVersion}[/]");
        return 0;
    }

    private string Channel(bool exact)
    {
        if (exact)
        {
            return $"version {Options.Version}";
        }

        return Options.PreRelease ? "version (including pre-releases)" : "full version";
    }

    private void WriteDowngradeWarning(ReleaseVersion currentVersion, ReleaseVersion latestVersion)
    {
        string message = currentVersion.IsPreRelease && !Options.PreRelease
            ? $"You are running pre-release {currentVersion}, which is newer than the latest full release {latestVersion}. "
                + $"Updating without --pre-release will downgrade PublishTool to {latestVersion}."
            : $"You are running {currentVersion}, which is newer than the latest published version {latestVersion}. "
                + $"Updating will downgrade PublishTool to {latestVersion}.";

        AnsiConsole.MarkupLine($"[yellow]{Markup.Escape(message)}[/]");
    }
}
