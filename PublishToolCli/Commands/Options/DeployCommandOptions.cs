using PublishTool.Attributes;
using PublishTool.Configuration;
using PublishTool.Helpers;

namespace PublishToolCli.Commands.Options;

public class DeployCommandOptions
{
    [Option("-dir", Parser = nameof(ParseWorkingDirectory), ValueName = "package dir path",
        Description = "Package directory created by publish (contains the manifest and one directory per project)")]
    public string WorkingDirectory { get; set; } = Directory.GetCurrentDirectory();

    [Option("-y", "--yes", Description = "Deploy. Without it the command is a dry run: it verifies the package, prints the plan "
        + "and exits 0 without changing anything")]
    public bool Yes { get; set; } = false;

    [Option("--dry-run", Description = "Only verify and print the plan, even with --yes (does not need administrator rights)")]
    public bool DryRun { get; set; } = false;

    [Option("--accept", Parser = nameof(ParseAccept), ValueName = "codes",
        Description = "Comma separated warning codes to accept; any other warning stops the deploy with exit code 3. "
        + "Codes: tool-version, target-missing, already-deployed, full-publish, unknown-base, no-record, chain-mismatch, config-changed, "
        + "target-modified")]
    public List<string> Accept { get; set; } = [];

    [Option("--target", Parser = nameof(ParseTarget), ValueName = "project=path",
        Description = "Absolute target directory of a project for this run instead of the stored one; repeat for more projects")]
    public Dictionary<string, TargetOverride> Targets { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    [Option("--service", Parser = nameof(ParseService), ValueName = "project=name",
        Description = "Windows service of a project for this run, empty name for an IIS app; repeat for more projects")]
    public Dictionary<string, TargetOverride> Services => Targets;

    [Option("--skip", Parser = nameof(ParseSkip), ValueName = "projects",
        Description = "Comma separated projects of the package not to deploy in this run, in addition to the ones stored as skipped "
        + "for the prefix; repeatable")]
    public List<string> Skip { get; set; } = [];

    [Option("--json", Description = "Print one JSON result object on stdout at the end; progress goes to stderr")]
    public bool Json { get; set; } = false;

    [Option("--verbose", Description = "Print every output line of the deploy steps")]
    public bool Verbose { get; set; } = false;

    public bool Executes => Yes && !DryRun;

    internal static bool ParseWorkingDirectory(string value, DeployCommandOptions options, out string? error)
    {
        if (!OptionParsers.TryResolveDirectory(value, options.WorkingDirectory, out string resolved, out error))
        {
            return false;
        }

        options.WorkingDirectory = resolved;
        return true;
    }

    internal static bool ParseAccept(string value, DeployCommandOptions options, out string? error) =>
        OptionValues.TryAddCodes(value, options.Accept, out error);

    internal static bool ParseTarget(string value, DeployCommandOptions options, out string? error) =>
        OptionValues.TryAddTarget(value, options.Targets, out error);

    internal static bool ParseService(string value, DeployCommandOptions options, out string? error) =>
        OptionValues.TryAddService(value, options.Targets, out error);

    internal static bool ParseSkip(string value, DeployCommandOptions options, out string? error) =>
        OptionValues.TryAddProjects(value, "--skip", options.Skip, out error);
}
