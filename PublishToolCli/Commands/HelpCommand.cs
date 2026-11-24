using PublishTool;
using PublishTool.Commands;
using PublishTool.Deployment;

namespace PublishToolCli.Commands;

public class HelpCommand(string helpText) : ICommand
{
    private const string ApplicationUsage = "Usage: PublishToolCli [command]";

    public Task<int> ExecuteAsync(CancellationToken ct)
    {
        System.Console.WriteLine($"PublishToolCli {BuildInfo.Version}");
        System.Console.WriteLine();
        System.Console.WriteLine(helpText);
        if (helpText.StartsWith(ApplicationUsage, StringComparison.Ordinal))
        {
            System.Console.WriteLine();
            System.Console.WriteLine("PublishToolCli never prompts: every input is an option, stored settings fill in the rest. "
                + "deploy and rollback only change the server with --yes.");
            System.Console.WriteLine();
            System.Console.WriteLine(ExitCodes.Help);
            System.Console.WriteLine();
            System.Console.WriteLine($"Warning codes for --accept: {string.Join(", ", WarningCodes.All)}");
        }

        return Task.FromResult(ExitCodes.Success);
    }
}
