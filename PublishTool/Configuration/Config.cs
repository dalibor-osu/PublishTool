using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace PublishTool.Configuration;

public class Config : IVersionedConfig
{
    public int Version { get; set; } = ConfigVersions.Current;

    public Dictionary<string, DirectorySettings> Directories { get; set; } = [];

    public DirectorySettings For(string path)
    {
        if (!Directories.TryGetValue(path, out var settings))
        {
            settings = new DirectorySettings();
            Directories[path] = settings;
        }

        return settings;
    }
}

public sealed class DirectorySettings
{
    public string? PublishDirectory { get; set; }

    public string? SharedDirectory { get; set; }

    public string? DeployDirectory { get; set; }

    public string[]? IgnoredDirectories { get; set; }

    public string[]? PublishableProjects { get; set; }

    [JsonIgnore]
    [MemberNotNullWhen(true, nameof(PublishDirectory), nameof(SharedDirectory), nameof(DeployDirectory), nameof(IgnoredDirectories),
        nameof(PublishableProjects))]
    public bool IsComplete =>
        PublishDirectory is not null
        && SharedDirectory is not null
        && DeployDirectory is not null
        && IgnoredDirectories is not null
        && PublishableProjects is not null;
}
