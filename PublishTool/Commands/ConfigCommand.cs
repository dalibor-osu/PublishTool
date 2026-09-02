using PublishTool.Attributes;
using PublishTool.Commands.Options;
using Spectre.Console;

namespace PublishTool.Commands;

[Command("config", Description = "Create or update the config in a directory")]
public class ConfigCommand(ConfigCommandOptions options) : ICommand<ConfigCommandOptions>
{
    public ConfigCommandOptions Options { get; } = options;

    public bool UsesAlternateScreen => true;

    public Task<int> ExecuteAsync(CancellationToken ct)
    {
        string currentWorkingDirectory = Options.WorkingDirectory;
        _ = ConfigHandler.Load(currentWorkingDirectory, true);
        AnsiConsole.MarkupLine("[green]Config updated successfully.[/]");
        return Task.FromResult(0);
    }
}