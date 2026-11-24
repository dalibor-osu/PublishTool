using PublishTool;
using PublishTool.Attributes;
using PublishTool.Helpers;

namespace PublishToolCli.Commands.Options;

public class PublishCommandOptions
{
    [Option("-dir", Parser = nameof(ParseWorkingDirectory), ValueName = "working dir path",
        Description = "Directory to scan for projects; its stored config is used (default: the current directory)")]
    public string WorkingDirectory { get; set; } = Directory.GetCurrentDirectory();

    [Option("-p", "--projects", Parser = nameof(ParseProjects), ValueName = "names",
        Description = "Comma separated names of the projects to publish, looked up among all projects in the directory "
        + "(default: every publishable project of the directory config)")]
    public List<string> Projects { get; set; } = [];

    [Option("-c", "--configuration", Parser = nameof(ParseConfiguration), ValueName = "Release|Debug",
        Description = "Build configuration (default: Release)")]
    public string Configuration { get; set; } = BuildConfigurations.Release;

    [Option("-l", "--local", Description = "Only publish into the publish directory and skip the deploy step (no shared or deploy "
        + "directory needed)")]
    public bool Local { get; set; } = false;

    [Option("--publish-dir", Parser = nameof(ParsePublishDirectory), ValueName = "path",
        Description = "Publish directory for this run instead of the stored one")]
    public string? PublishDirectory { get; set; }

    [Option("--shared-dir", Parser = nameof(ParseSharedDirectory), ValueName = "path",
        Description = "Shared directory (reference zips) for this run instead of the stored one")]
    public string? SharedDirectory { get; set; }

    [Option("--deploy-dir", Parser = nameof(ParseDeployDirectory), ValueName = "path",
        Description = "Deploy directory (change packages) for this run instead of the stored one")]
    public string? DeployDirectory { get; set; }

    [Option("--ignore", Parser = nameof(ParseIgnored), ValueName = "dirs",
        Description = "Comma separated directory names skipped while scanning, for this run (default: the stored ones, else .git, .claude)")]
    public string[]? IgnoredDirectories { get; set; }

    [Option("--json", Description = "Print one JSON result object on stdout at the end; progress goes to stderr")]
    public bool Json { get; set; } = false;

    [Option("--verbose", Description = "Print every output line of MSBuild and the deploy steps")]
    public bool Verbose { get; set; } = false;

    internal static bool ParseWorkingDirectory(string value, PublishCommandOptions options, out string? error)
    {
        if (!OptionParsers.TryResolveDirectory(value, options.WorkingDirectory, out string resolved, out error))
        {
            return false;
        }

        options.WorkingDirectory = resolved;
        return true;
    }

    internal static bool ParseProjects(string value, PublishCommandOptions options, out string? error)
    {
        options.Projects.AddRange(OptionValues.SplitList(value));
        error = options.Projects.Count == 0 ? "--projects needs at least one project name" : null;
        return error is null;
    }

    internal static bool ParseConfiguration(string value, PublishCommandOptions options, out string? error)
    {
        string? configuration = BuildConfigurations.Find(value);
        error = configuration is null ? $"Unknown configuration '{value}', use {string.Join(" or ", BuildConfigurations.All)}" : null;
        options.Configuration = configuration ?? options.Configuration;
        return configuration is not null;
    }

    internal static bool ParsePublishDirectory(string value, PublishCommandOptions options, out string? error)
    {
        bool parsed = OptionValues.TryResolvePath(value, out string resolved, out error);
        options.PublishDirectory = resolved;
        return parsed;
    }

    internal static bool ParseSharedDirectory(string value, PublishCommandOptions options, out string? error)
    {
        bool parsed = OptionValues.TryResolvePath(value, out string resolved, out error);
        options.SharedDirectory = resolved;
        return parsed;
    }

    internal static bool ParseDeployDirectory(string value, PublishCommandOptions options, out string? error)
    {
        bool parsed = OptionValues.TryResolvePath(value, out string resolved, out error);
        options.DeployDirectory = resolved;
        return parsed;
    }

    internal static bool ParseIgnored(string value, PublishCommandOptions options, out string? error)
    {
        options.IgnoredDirectories = OptionValues.SplitList(value);
        error = null;
        return true;
    }
}
