namespace PublishTool.Commands;

public class HelpCommand(string helpText) : ICommand
{
    public bool UsesAlternateScreen => false;

    public Task<int> ExecuteAsync(CancellationToken ct)
    {
        System.Console.WriteLine($"PublishTool {BuildInfo.Version}");
        System.Console.WriteLine();
        System.Console.WriteLine(helpText);
        return Task.FromResult(0);
    }
}
