using PublishTool.Attributes;

namespace PublishTool.Commands.Options;

public class UpdateCommandOptions
{
    [Option("-v", "--version", Description = "Install this exact version (e.g. 1.4.0 or v1.4.0-rc.2), also when it is older than "
        + "the installed one", ValueName = "version")]
    public string? Version { get; set; }

    [Option("-pre", "--pre-release", Description = "Use the newest release including pre-releases (a newer full release is still preferred). "
        + "Without this flag, the newest release not marked as a pre-release is used")]
    public bool PreRelease { get; set; } = false;

    [Option("--fetch", Description = "Only report the version that would be installed, do not install it")]
    public bool Fetch { get; set; } = false;
}
