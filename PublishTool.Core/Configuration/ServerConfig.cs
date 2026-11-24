namespace PublishTool.Configuration;

public class ServerConfig : IVersionedConfig
{
    public int Version { get; set; } = ServerConfigVersions.Current;

    public Dictionary<string, DeployEnvironment> Environments { get; set; } = [];

    public Dictionary<string, DeployRecord> Deployed { get; set; } = [];

    public DeployEnvironment EnvironmentFor(string prefix)
    {
        string? key = FindKey(Environments, prefix);
        if (key is not null)
        {
            return Environments[key];
        }

        var environment = new DeployEnvironment();
        Environments[prefix] = environment;
        return environment;
    }

    public DeployEnvironment? FindEnvironment(string prefix)
    {
        string? key = FindKey(Environments, prefix);
        return key is null ? null : Environments[key];
    }

    public DeployRecord? RecordFor(string targetPath)
    {
        string? key = FindKey(Deployed, targetPath);
        return key is null ? null : Deployed[key];
    }

    public void SetRecord(string targetPath, DeployRecord? record)
    {
        string? key = FindKey(Deployed, targetPath);
        if (key is not null)
        {
            Deployed.Remove(key);
        }

        if (record is not null)
        {
            Deployed[targetPath] = record;
        }
    }

    public static string? FindKey<TValue>(Dictionary<string, TValue> dictionary, string key) =>
        dictionary.Keys.FirstOrDefault(k => string.Equals(k, key, StringComparison.OrdinalIgnoreCase));
}

public sealed class DeployEnvironment
{
    public Dictionary<string, DeployTarget> Targets { get; set; } = [];

    public DeployTarget? TargetFor(string project)
    {
        string? key = ServerConfig.FindKey(Targets, project);
        return key is null ? null : Targets[key];
    }
}

public sealed class DeployTarget
{
    public string? Path { get; set; }

    public string? ServiceName { get; set; }
}

public sealed class DeployRecord
{
    public string Package { get; set; } = string.Empty;

    public DateTimeOffset Time { get; set; }
}
