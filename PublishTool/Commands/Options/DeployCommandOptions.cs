using PublishTool.Attributes;
using PublishTool.Helpers;

namespace PublishTool.Commands.Options;

public class DeployCommandOptions
{
    [Option("-dir", Parser = nameof(ParseWorkingDirectory), ValueName = "package dir path",
        Description = "Package directory created by publish (contains the manifest and one directory per project)")]
    public string WorkingDirectory { get; set; } = Directory.GetCurrentDirectory();

    [Option("-y", "--yes", Description = "Do not ask for confirmation; stop with exit code 1 if anything needs to be accepted "
        + "(warnings, missing target mappings). Prints the result at the end instead of a live display")]
    public bool Yes { get; set; } = false;

    [Option("--dry-run", Description = "Verify the package and print what would be deployed, then exit without changing anything "
        + "(does not need administrator rights)")]
    public bool DryRun { get; set; } = false;

    internal static bool ParseWorkingDirectory(string value, DeployCommandOptions options, out string? error)
    {
        if (!OptionParsers.TryResolveDirectory(value, options.WorkingDirectory, out string resolved, out error))
        {
            return false;
        }

        options.WorkingDirectory = resolved;
        return true;
    }
}
