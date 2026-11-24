using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace PublishTool.Configuration;

public interface IConfigParser
{
    public int Version { get; }
    public object Parse(string json);
}

public sealed class ConfigParser<TModel>(int version, JsonTypeInfo<TModel> typeInfo) : IConfigParser where TModel : class
{
    public int Version => version;

    public object Parse(string json) =>
        JsonSerializer.Deserialize(json, typeInfo)
        ?? throw new ConfigException($"The config file could not be read as format version {version}.");
}
