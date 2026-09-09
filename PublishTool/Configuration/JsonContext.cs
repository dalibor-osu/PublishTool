using System.Text.Json.Serialization;

namespace PublishTool.Configuration;

/// <summary>
/// Source-generated JSON metadata (required for Native AOT). Every config model that can still be read, i.e. the current one and
/// each frozen older version, has to be registered here.
/// </summary>
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(ConfigEnvelope))]
[JsonSerializable(typeof(Config))]
public partial class JsonContext : JsonSerializerContext;
