using PublishTool.Attributes;

namespace PublishTool.Commands.Options;

public class UpdateCommandOptions
{
    [Option("-v", "--version", Description = "Install this exact version", ValueName = "version")]
    public string? Version { get; set; }

    [Option("-pre", "--pre-release", Description = "Include pre-release versions")]
    public bool PreRelease { get; set; } = false;

    [Option("--fetch", Description = "Only report the latest version, do not install it")]
    public bool Fetch { get; set; } = false;
}