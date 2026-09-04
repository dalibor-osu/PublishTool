using PublishTool.Attributes;
using PublishTool.Helpers;

namespace PublishTool.Commands.Options;

public class PublishCommandOptions
{
    [Option("-dir", Parser = nameof(ParseWorkingDirectory), ValueName = "working dir path", Description = "Directory to scan for projects")]
    public string WorkingDirectory { get; set; } = Directory.GetCurrentDirectory();

    [Option("-a", Description = "Publish every project in Release configuration without asking")]
    public bool All { get; set; } = false;

    [Option("-c", "--complete", Description = "After publishing, compare the publish with the previous one of the current git "
        + "branch in the shared directory and create a directory named after the branch and time (e.g. PROD_2026-09-03_15-03-43) "
        + "in the deploy directory with only the changed files. The full publish is stored under the same name in the shared "
        + "directory as the reference for the next comparison")]
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