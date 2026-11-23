using PublishTool.Attributes;
using PublishTool.Helpers;

namespace PublishTool.Commands.Options;

public class RollbackCommandOptions
{
    [Option("-dir", Parser = nameof(ParseWorkingDirectory), ValueName = "package dir path",
        Description = "Package directory whose deploy is rolled back (contains the .backup directory)")]
    public string WorkingDirectory { get; set; } = Directory.GetCurrentDirectory();

    [Option("-y", "--yes", Description = "Do not ask for confirmation; stop with exit code 1 if anything needs to be accepted. "
        + "Prints the result at the end instead of a live display")]
    public bool Yes { get; set; } = false;

    internal static bool ParseWorkingDirectory(string value, RollbackCommandOptions options, out string? error)
    {
        if (!OptionParsers.TryResolveDirectory(value, options.WorkingDirectory, out string resolved, out error))
        {
            return false;
        }

        options.WorkingDirectory = resolved;
        return true;
    }
}
