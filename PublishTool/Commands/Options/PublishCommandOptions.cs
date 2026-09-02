using PublishTool.Attributes;
using PublishTool.Helpers;

namespace PublishTool.Commands.Options;

public class PublishCommandOptions
{
    [Option("-dir", Parser = nameof(ParseWorkingDirectory), ValueName = "working dir path",
        Description = "Directory to scan for projects")]
    public string WorkingDirectory { get; set; } = Directory.GetCurrentDirectory();

    [Option("-a", Description = "Publish every project without asking")]
    public bool All { get; set; } = false;

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