namespace PublishTool.Commands;

public class VersionCommand(VersionCommand.Options options) : Command<VersionCommand.Options>(options)
{
    public new class Options
    { }

    public override bool UsesAlternateScreen => false;

    public override Task<int> ExecuteAsync(CancellationToken ct)
    {
        System.Console.WriteLine($"PublishTool version {BuildInfo.Version}"); // Seems to be faster than AnsiConsole
        return Task.FromResult(0);
    }
}