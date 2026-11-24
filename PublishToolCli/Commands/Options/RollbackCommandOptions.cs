using PublishTool.Attributes;
using PublishTool.Helpers;

namespace PublishToolCli.Commands.Options;

public class RollbackCommandOptions
{
    [Option("-dir", Parser = nameof(ParseWorkingDirectory), ValueName = "package dir path",
        Description = "Package directory whose deploy is rolled back (contains the .backup directory)")]
    public string WorkingDirectory { get; set; } = Directory.GetCurrentDirectory();

    [Option("-y", "--yes", Description = "Roll back. Without it the command is a dry run: it prints what would be restored and exits 0 "
        + "without changing anything")]
    public bool Yes { get; set; } = false;

    [Option("--accept", Parser = nameof(ParseAccept), ValueName = "codes",
        Description = "Comma separated warning codes to accept; any other warning stops the rollback with exit code 3. Codes: newer-deploy")]
    public List<string> Accept { get; set; } = [];

    [Option("--json", Description = "Print one JSON result object on stdout at the end; progress goes to stderr")]
    public bool Json { get; set; } = false;

    [Option("--verbose", Description = "Print every output line of the rollback steps")]
    public bool Verbose { get; set; } = false;

    internal static bool ParseWorkingDirectory(string value, RollbackCommandOptions options, out string? error)
    {
        if (!OptionParsers.TryResolveDirectory(value, options.WorkingDirectory, out string resolved, out error))
        {
            return false;
        }

        options.WorkingDirectory = resolved;
        return true;
    }

    internal static bool ParseAccept(string value, RollbackCommandOptions options, out string? error) =>
        OptionValues.TryAddCodes(value, options.Accept, out error);
}
