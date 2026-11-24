using PublishTool.Attributes;

namespace PublishToolCli.Commands.Options;

public class UpdateCommandOptions
{
    [Option("-v", "--version", ValueName = "version",
        Description = "Install this exact version (e.g. 1.4.0 or v1.4.0-rc.2), also when it is older than the installed one")]
    public string? Version { get; set; }

    [Option("-pre", "--pre-release", Description = "Use the newest release including pre-releases (a newer full release is still preferred). "
        + "Without this flag, the newest release not marked as a pre-release is used")]
    public bool PreRelease { get; set; } = false;

    [Option("--fetch", Description = "Only report the version that would be installed, do not install it")]
    public bool Fetch { get; set; } = false;

    [Option("--accept", Parser = nameof(ParseAccept), ValueName = "codes",
        Description = "Comma separated warning codes to accept; 'downgrade' allows installing a version older than the running one")]
    public List<string> Accept { get; set; } = [];

    [Option("--json", Description = "Print one JSON result object on stdout at the end; progress goes to stderr")]
    public bool Json { get; set; } = false;

    internal static bool ParseAccept(string value, UpdateCommandOptions options, out string? error) =>
        PublishToolCli.OptionValues.TryAddCodes(value, options.Accept, out error);
}
