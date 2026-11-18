using PublishTool.Attributes;
using PublishTool.Helpers;

namespace PublishTool.Commands.Options;

public class PublishCommandOptions
{
    [Option("-dir", Parser = nameof(ParseWorkingDirectory), ValueName = "working dir path", Description = "Directory to scan for projects")]
    public string WorkingDirectory { get; set; } = Directory.GetCurrentDirectory();

    [Option("-a", Description = "Publish every project in Release configuration without asking")]
    public bool All { get; set; } = false;

    [Option("-l", "--local", Description = "Only publish into the publish directory and skip the deploy step. Without this flag, "
        + "the publish is compared with the previous one of the current git branch in the shared directory, a directory named after "
        + "the branch and time (e.g. PROD_2026-09-03_15-03-43) with only the changed files is created in the deploy directory, and the "
        + "full publish is stored as a zip archive of the same name in the shared directory as the reference for the next comparison; "
        + "only the 3 newest archives of the branch are kept there")]
    public bool Local { get; set; } = false;

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