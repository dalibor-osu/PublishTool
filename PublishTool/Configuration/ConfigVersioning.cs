using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace PublishTool.Configuration;

/// <summary>Implemented by the current config model so the store can stamp the format version before saving.</summary>
public interface IVersionedConfig
{
    int Version { get; set; }
}

/// <summary>Only the version field of a config file; used to pick the parser before the rest is read. Unknown members are ignored.</summary>
public sealed class ConfigEnvelope
{
    /// <summary>Null for files written before versioning existed; those are format version 1.</summary>
    public int? Version { get; set; }
}

/// <summary>Reads a config file of exactly one format version into the model of that version.</summary>
public interface IConfigParser
{
    int Version { get; }
    object Parse(string json);
}

public sealed class ConfigParser<TModel>(int version, JsonTypeInfo<TModel> typeInfo) : IConfigParser where TModel : class
{
    public int Version => version;

    public object Parse(string json) =>
        JsonSerializer.Deserialize(json, typeInfo)
        ?? throw new ConfigException($"The config file could not be read as format version {version}.");
}

/// <summary>Converts the model of format version <see cref="From"/> into the model of version <see cref="From"/> + 1.</summary>
public interface IConfigMigrator
{
    int From { get; }
    object Migrate(object source);
}

/// <summary>Typed base class for migrators; derive from it instead of implementing <see cref="IConfigMigrator"/> directly.</summary>
public abstract class ConfigMigrator<TFrom, TTo> : IConfigMigrator where TFrom : class where TTo : class
{
    public abstract int From { get; }

    public abstract TTo Migrate(TFrom source);

    object IConfigMigrator.Migrate(object source) => Migrate(source as TFrom
        ?? throw new InvalidOperationException(
            $"Config migrator from version {From} expected a {typeof(TFrom).Name} but received {source.GetType().Name}."));
}

/// <summary>A config file problem the user has to act on; the message is shown as is.</summary>
public sealed class ConfigException(string message, Exception? inner = null) : Exception(message, inner);
