using Spectre.Console;

namespace PublishTool.Commands;

public class ConfigCommand(ConfigCommand.Options options) : Command<ConfigCommand.Options>(options)
{
    public new class Options
    {
        public string WorkingDirectory { get; set; } = Directory.GetCurrentDirectory();
    }


    public override bool UsesAlternateScreen => true;

    public override Task<int> ExecuteAsync(CancellationToken ct)
    {
        string currentWorkingDirectory = base.Options.WorkingDirectory;
        _ = ConfigHandler.Load(currentWorkingDirectory, true);
        AnsiConsole.MarkupLine("[green]Config updated successfully.[/]");
        return Task.FromResult(0);
    }
}