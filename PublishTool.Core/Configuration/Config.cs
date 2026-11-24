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
    public static readonly IReadOnlyList<string> DefaultIgnoredDirectories = [".git", ".claude"];

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

    public DirectorySettings With(SettingsOverrides overrides) => new()
    {
        PublishDirectory = overrides.PublishDirectory ?? PublishDirectory,
        SharedDirectory = overrides.SharedDirectory ?? SharedDirectory,
        DeployDirectory = overrides.DeployDirectory ?? DeployDirectory,
        IgnoredDirectories = overrides.IgnoredDirectories ?? IgnoredDirectories,
        PublishableProjects = overrides.PublishableProjects ?? PublishableProjects
    };

    public IReadOnlyList<string> MissingFor(bool local, bool projectsGiven)
    {
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(PublishDirectory))
        {
            missing.Add(SettingKeys.PublishDirectory);
        }

        if (!local && string.IsNullOrWhiteSpace(SharedDirectory))
        {
            missing.Add(SettingKeys.SharedDirectory);
        }

        if (!local && string.IsNullOrWhiteSpace(DeployDirectory))
        {
            missing.Add(SettingKeys.DeployDirectory);
        }

        if (!projectsGiven && PublishableProjects is null)
        {
            missing.Add(SettingKeys.PublishableProjects);
        }

        return missing;
    }
}

public sealed class SettingsOverrides
{
    public string? PublishDirectory { get; set; }

    public string? SharedDirectory { get; set; }

    public string? DeployDirectory { get; set; }

    public string[]? IgnoredDirectories { get; set; }

    public string[]? PublishableProjects { get; set; }
}

public static class SettingKeys
{
    public const string PublishDirectory = "publish-dir";
    public const string SharedDirectory = "shared-dir";
    public const string DeployDirectory = "deploy-dir";
    public const string IgnoredDirectories = "ignore";
    public const string PublishableProjects = "publishable";
}
