namespace PublishTool.Configuration.Versions;

public sealed class ConfigV1
{
    public int Version { get; set; } = 1;
    public Dictionary<string, string> PublishDirectories { get; set; } = [];
    public Dictionary<string, string[]> IgnoredDirectories { get; set; } = [];
    public Dictionary<string, string[]> PublishableProjects { get; set; } = [];
    public Dictionary<string, string> SharedDirectories { get; set; } = [];
    public Dictionary<string, string> DeployDirectories { get; set; } = [];
}
