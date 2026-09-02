using PublishTool.Attributes;
using PublishTool.Helpers;

namespace PublishTool.Commands.Options;

public class ConfigCommandOptions
{
    [Option("-dir", Parser = nameof(ParseWorkingDirectory), ValueName = "working dir path", Description = "Directory the config belongs to")]
    public string WorkingDirectory { get; set; } = Directory.GetCurrentDirectory();

    internal static bool ParseWorkingDirectory(string value, ConfigCommandOptions options, out string? error)
    {
        if (!OptionParsers.TryResolveDirectory(value, options.WorkingDirectory, out string resolved, out error))
        {
            return false;
        }

        options.WorkingDirectory = resolved;
        return true;
    }
}