using PublishTool.Attributes;
using PublishTool.Commands.Options;

namespace PublishTool.Commands;

[Command("version", Description = "Print the version of this tool")]
public class VersionCommand(VersionCommandOptions options) : ICommand<VersionCommandOptions>
{
    public VersionCommandOptions Options { get; } = options;

    public bool UsesAlternateScreen => false;

    public Task<int> ExecuteAsync(CancellationToken ct)
    {
        System.Console.WriteLine($"PublishTool version {BuildInfo.Version}"); // Seems to be faster than AnsiConsole
        return Task.FromResult(0);
    }
}