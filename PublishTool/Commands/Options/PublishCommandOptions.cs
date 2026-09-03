using PublishTool.Attributes;
using PublishTool.Helpers;

namespace PublishTool.Commands.Options;

public class PublishCommandOptions
{
    [Option("-dir", Parser = nameof(ParseWorkingDirectory), ValueName = "working dir path", Description = "Directory to scan for projects")]
    public string WorkingDirectory { get; set; } = Directory.GetCurrentDirectory();

    [Option("-a", Description = "Publish every project in Release configuration without asking")]
    public bool All { get; set; } = false;

    [Option("-c", "--complete", Description = "After publishing, create a time-stamped directory in the deploy directory "
        + "(named after the current git branch, e.g. PROD_2026-09-03_15-03-43) with the full publish, and a Deploy "
        + "subdirectory with only the files that differ from the previous deploy of the same branch")]
    public bool Complete { get; set; } = false;

    internal static bool ParseWorkingDirectory(string value, PublishCommandOptions options, out string? error)
    {
        if (!OptionParsers.TryResolveDirectory(value, options.WorkingDirectory, out string resolved, out error))
        {
            return false;
        }

        options.WorkingDirectory = resolved;
        return true;
    }
}