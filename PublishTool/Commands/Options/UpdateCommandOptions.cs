using PublishTool.Attributes;

namespace PublishTool.Commands.Options;

public class UpdateCommandOptions
{
    [Option("-v", "--version", Description = "Install this exact version", ValueName = "version")]
    public string? Version { get; set; }

    [Option("-pre", "--pre-release", Description = "Use the newest release including pre-releases (a newer full release is still preferred). "
        + "Without this flag, the newest release not marked as a pre-release is used")]
    public bool PreRelease { get; set; } = false;

    [Option("--fetch", Description = "Only report the latest version, do not install it")]
    public bool Fetch { get; set; } = false;
}