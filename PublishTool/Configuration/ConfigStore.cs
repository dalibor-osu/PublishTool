using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace PublishTool.Configuration;

public static class ConfigStore
{
    public static readonly string Directory =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PublishTool");

    public static readonly ConfigStore<Config> Default = new(
        Path.Combine(Directory, "config.json"),
        ConfigVersions.Current,
        ConfigVersions.Parsers,
        ConfigVersions.Migrators,
        JsonContext.Default.Config);

    public static readonly string ServerDirectory =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "PublishTool");

    public static readonly ConfigStore<ServerConfig> Server = new(
        Path.Combine(ServerDirectory, "server.json"),
        ServerConfigVersions.Current,
        ServerConfigVersions.Parsers,
        ServerConfigVersions.Migrators,
        JsonContext.Default.ServerConfig);
}

public sealed class ConfigStore<TConfig> where TConfig : class, IVersionedConfig
{
    private const int FirstVersion = 1;
    private readonly int _currentVersion;
    private readonly Dictionary<int, IConfigParser> _parsers;
    private readonly Dictionary<int, IConfigMigrator> _migrators;
    private readonly JsonTypeInfo<TConfig> _currentTypeInfo;

    public ConfigStore(
        string path,
        int currentVersion,
        IReadOnlyList<IConfigParser> parsers,
        IReadOnlyList<IConfigMigrator> migrators,
        JsonTypeInfo<TConfig> currentTypeInfo)
    {
        Path = path;
        _currentVersion = currentVersion;
        _currentTypeInfo = currentTypeInfo;
        _parsers = parsers.ToDictionary(p => p.Version);
        _migrators = migrators.ToDictionary(m => m.From);
        Validate();
    }

    public string Path { get; }

    public string BackupPath(int version) =>
        System.IO.Path.Combine(
            System.IO.Path.GetDirectoryName(Path)!,
            $"{System.IO.Path.GetFileNameWithoutExtension(Path)}.v{version}{System.IO.Path.GetExtension(Path)}.bak");

    public static int ReadVersion(string json)
    {
        var envelope = JsonSerializer.Deserialize(json, JsonContext.Default.ConfigEnvelope);
        return envelope?.Version ?? FirstVersion;
    }

    public TConfig Load()
    {
        if (!File.Exists(Path))
        {
            return CreateEmpty();
        }

        string json;
        int version;
        try
        {
            json = File.ReadAllText(Path);
            version = ReadVersion(json);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            throw new ConfigException($"The config file {Path} could not be read: {e.Message}", e);
        }

        if (version > _currentVersion)
        {
            throw new ConfigException(
                $"The config file {Path} has format version {version}, but this PublishTool only understands versions up to "
                + $"{_currentVersion}. Update PublishTool, or delete the file to start over.");
        }

        if (version < FirstVersion)
        {
            throw new ConfigException($"The config file {Path} has an invalid format version {version}.");
        }

        object model;
        try
        {
            model = _parsers[version].Parse(json);
            for (int step = version; step < _currentVersion; step++)
            {
                model = _migrators[step].Migrate(model);
            }
        }
        catch (JsonException e)
        {
            throw new ConfigException($"The config file {Path} (format version {version}) is not valid: {e.Message}", e);
        }

        if (model is not TConfig config)
        {
            throw new InvalidOperationException(
                $"Config migration ended with a {model.GetType().Name} instead of {typeof(TConfig).Name}; check the migrator chain.");
        }

        if (version < _currentVersion)
        {
            Logger.LogInfo($"Migrated config {Path} from format version {version} to {_currentVersion}");
            File.Copy(Path, BackupPath(version), overwrite: true);
            Save(config);
        }

        return config;
    }

    public void Save(TConfig config)
    {
        config.Version = _currentVersion;
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        File.WriteAllText(Path, JsonSerializer.Serialize(config, _currentTypeInfo));
    }

    private TConfig CreateEmpty() =>
        JsonSerializer.Deserialize("{}", _currentTypeInfo)
        ?? throw new InvalidOperationException($"Could not create an empty {typeof(TConfig).Name}");

    private void Validate()
    {
        if (_currentVersion < FirstVersion)
        {
            throw new InvalidOperationException($"The current config version has to be at least {FirstVersion}");
        }

        for (int version = FirstVersion; version <= _currentVersion; version++)
        {
            if (!_parsers.ContainsKey(version))
            {
                throw new InvalidOperationException($"No config parser is registered for format version {version}");
            }

            if (version < _currentVersion && !_migrators.ContainsKey(version))
            {
                throw new InvalidOperationException($"No config migrator is registered from format version {version} to {version + 1}");
            }
        }

        int newestVersion = _parsers.Keys.Max();
        if (newestVersion > _currentVersion)
        {
            throw new InvalidOperationException($"A config parser is registered for version {newestVersion}, which is newer than the current {_currentVersion}");
        }
    }
}
