namespace PublishTool.Configuration;

/// <summary>
/// The current config file model (format version <see cref="ConfigVersions.Current"/>). Every dictionary is keyed by the absolute
/// working directory the settings belong to. Adding a new dictionary is NOT a new format version: a missing key simply
/// deserializes as empty and <see cref="ConfigHandler"/> asks the user for it, exactly like a directory that was never set up.
/// See <see cref="ConfigVersions"/> for when and how to introduce a new version.
/// </summary>
public class Config : IVersionedConfig
{
    public int Version { get; set; } = ConfigVersions.Current;
    public Dictionary<string, string> PublishDirectories { get; set; } = [];
    public Dictionary<string, string[]> IgnoredDirectories { get; set; } = [];
    public Dictionary<string, string[]> PublishableProjects { get; set; } = [];
    public Dictionary<string, string> SharedDirectories { get; set; } = [];
    public Dictionary<string, string> DeployDirectories { get; set; } = [];
}
