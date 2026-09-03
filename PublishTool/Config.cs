namespace PublishTool;

public class Config
{
    public Dictionary<string, string> PublishDirectories { get; set; } = [];
    public Dictionary<string, string[]> IgnoredDirectories { get; set; } = [];
    public Dictionary<string, string[]> PublishableProjects { get; set; } = [];
    public Dictionary<string, string> DeployDirectories { get; set; } = [];
}