using System.Text.Json.Serialization;
using PublishTool.Configuration.Versions;

namespace PublishTool.Configuration;

[JsonSourceGenerationOptions(WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ConfigEnvelope))]
[JsonSerializable(typeof(ConfigV1))]
[JsonSerializable(typeof(Config))]
public partial class JsonContext : JsonSerializerContext;
