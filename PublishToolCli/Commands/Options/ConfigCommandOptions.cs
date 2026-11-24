using PublishTool.Attributes;
using PublishTool.Configuration;
using PublishTool.Helpers;

namespace PublishToolCli.Commands.Options;

public class ConfigCommandOptions
{
    [Option("-dir", Parser = nameof(ParseWorkingDirectory), ValueName = "working dir path",
        Description = "Directory whose settings are shown or changed (default: the current directory)")]
    public string WorkingDirectory { get; set; } = Directory.GetCurrentDirectory();

    [Option("--publish-dir", Parser = nameof(ParsePublishDirectory), ValueName = "path", Description = "Set the publish directory")]
    public string? PublishDirectory { get; set; }

    [Option("--shared-dir", Parser = nameof(ParseSharedDirectory), ValueName = "path", Description = "Set the shared directory")]
    public string? SharedDirectory { get; set; }

    [Option("--deploy-dir", Parser = nameof(ParseDeployDirectory), ValueName = "path", Description = "Set the deploy directory")]
    public string? DeployDirectory { get; set; }

    [Option("--ignore", Parser = nameof(ParseIgnored), ValueName = "dirs",
        Description = "Set the comma separated directory names skipped while scanning")]
    public string[]? IgnoredDirectories { get; set; }

    [Option("--publishable", Parser = nameof(ParsePublishable), ValueName = "names",
        Description = "Set the comma separated names of the projects publish uses by default; they must exist in the directory")]
    public string[]? PublishableProjects { get; set; }

    [Option("--unset", Parser = nameof(ParseUnset), ValueName = "settings",
        Description = "Comma separated settings to remove: publish-dir, shared-dir, deploy-dir, ignore, publishable")]
    public List<string> Unset { get; set; } = [];

    [Option("--prefix", ValueName = "package prefix",
        Description = "Show or change the server deploy targets of packages with this prefix (the git branch name) instead of directory settings")]
    public string? Prefix { get; set; }

    [Option("--target", Parser = nameof(ParseTarget), ValueName = "project=path",
        Description = "With --prefix: set the absolute target directory of a project; repeat for more projects")]
    public Dictionary<string, TargetOverride> Targets { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    [Option("--service", Parser = nameof(ParseService), ValueName = "project=name",
        Description = "With --prefix: set the Windows service of a project, empty name for an IIS app; repeat for more projects")]
    public Dictionary<string, TargetOverride> Services => Targets;

    [Option("--remove-target", Parser = nameof(ParseRemoveTarget), ValueName = "project",
        Description = "With --prefix: remove the target of a project; repeat for more projects")]
    public List<string> RemoveTargets { get; set; } = [];

    [Option("--json", Description = "Print one JSON result object on stdout")]
    public bool Json { get; set; } = false;

    public bool ChangesDirectory =>
        PublishDirectory is not null || SharedDirectory is not null || DeployDirectory is not null || IgnoredDirectories is not null
        || PublishableProjects is not null || Unset.Count > 0;

    public bool ChangesTargets => Targets.Count > 0 || RemoveTargets.Count > 0;

    internal static bool ParseWorkingDirectory(string value, ConfigCommandOptions options, out string? error)
    {
        if (!OptionParsers.TryResolveDirectory(value, options.WorkingDirectory, out string resolved, out error))
        {
            return false;
        }

        options.WorkingDirectory = resolved;
        return true;
    }

    internal static bool ParsePublishDirectory(string value, ConfigCommandOptions options, out string? error)
    {
        bool parsed = OptionValues.TryResolvePath(value, out string resolved, out error);
        options.PublishDirectory = resolved;
        return parsed;
    }

    internal static bool ParseSharedDirectory(string value, ConfigCommandOptions options, out string? error)
    {
        bool parsed = OptionValues.TryResolvePath(value, out string resolved, out error);
        options.SharedDirectory = resolved;
        return parsed;
    }

    internal static bool ParseDeployDirectory(string value, ConfigCommandOptions options, out string? error)
    {
        bool parsed = OptionValues.TryResolvePath(value, out string resolved, out error);
        options.DeployDirectory = resolved;
        return parsed;
    }

    internal static bool ParseIgnored(string value, ConfigCommandOptions options, out string? error)
    {
        options.IgnoredDirectories = OptionValues.SplitList(value);
        error = null;
        return true;
    }

    internal static bool ParsePublishable(string value, ConfigCommandOptions options, out string? error)
    {
        options.PublishableProjects = OptionValues.SplitList(value);
        error = null;
        return true;
    }

    internal static bool ParseUnset(string value, ConfigCommandOptions options, out string? error)
    {
        string[] known = [SettingKeys.PublishDirectory, SettingKeys.SharedDirectory, SettingKeys.DeployDirectory, SettingKeys.IgnoredDirectories,
            SettingKeys.PublishableProjects];
        foreach (string key in OptionValues.SplitList(value))
        {
            if (!known.Contains(key, StringComparer.OrdinalIgnoreCase))
            {
                error = $"Unknown setting '{key}', use {string.Join(", ", known)}";
                return false;
            }

            options.Unset.Add(key.ToLowerInvariant());
        }

        error = null;
        return true;
    }

    internal static bool ParseTarget(string value, ConfigCommandOptions options, out string? error) =>
        OptionValues.TryAddTarget(value, options.Targets, out error);

    internal static bool ParseService(string value, ConfigCommandOptions options, out string? error) =>
        OptionValues.TryAddService(value, options.Targets, out error);

    internal static bool ParseRemoveTarget(string value, ConfigCommandOptions options, out string? error)
    {
        options.RemoveTargets.AddRange(OptionValues.SplitList(value));
        error = options.RemoveTargets.Count == 0 ? "--remove-target needs a project name" : null;
        return error is null;
    }
}
