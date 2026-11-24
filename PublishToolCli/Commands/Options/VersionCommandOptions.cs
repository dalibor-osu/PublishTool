using PublishTool.Attributes;

namespace PublishToolCli.Commands.Options;

public class VersionCommandOptions
{
    [Option("--json", Description = "Print the version as a JSON result object")]
    public bool Json { get; set; } = false;
}
