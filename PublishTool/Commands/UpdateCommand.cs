using PublishTool.GitHub;
using PublishTool.Helpers;
using Spectre.Console;

namespace PublishTool.Commands;

public class UpdateCommand(UpdateCommand.Options options) : Command<UpdateCommand.Options>(options)
{
    public new class Options
    {
        public string? Version { get; set; }
        public bool PreRelease { get; set; } = false;
        public bool Fetch { get; set; } = false;
    }

    public override bool UsesAlternateScreen => false;


    public override async Task<int> ExecuteAsync(CancellationToken ct)
    {
        var client = new GitHubClient();
        if (base.Options.Fetch)
        {
            string preRelease = base.Options.PreRelease ? "pre-release" : "full";
            AnsiConsole.WriteLine($"Fetching latest {preRelease} version...");
            var result = base.Options.PreRelease ? await client.GetLatestVersionIncludingPreReleaseAsync(ct) : await client.GetLatestFullVersionAsync(ct);
            if (!result.IsSuccess)
            {
                AnsiConsole.MarkupLine($"[red]Failed to fetch latest {preRelease} version:[/] {result.Error}");
                return 1;
            }

            AnsiConsole.WriteLine($"Latest {preRelease} version: {result.Value} (Current: {BuildInfo.Version})");
            return 0;
        }

        if (EnvironmentHelper.IsDevVersion)
        {
            AnsiConsole.WriteLine("Cannot update dev version");
            return 1;
        }

        return 0;
    }
}