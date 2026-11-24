namespace PublishTool.Commands;

public class HelpCommand(string helpText) : ICommand
{
    private const string ApplicationUsage = "Usage: PublishTool [command]";

    public Task<int> ExecuteAsync(CancellationToken ct)
    {
        System.Console.WriteLine($"PublishTool {BuildInfo.Version}");
        System.Console.WriteLine();
        System.Console.WriteLine(helpText);
        if (helpText.StartsWith(ApplicationUsage, StringComparison.Ordinal))
        {
            System.Console.WriteLine();
            System.Console.WriteLine("PublishTool is interactive. For scripts and AI agents use PublishToolCli, which takes every input as an "
                + "option and never prompts ('PublishToolCli --help').");
        }

        return Task.FromResult(0);
    }
}
