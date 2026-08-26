using System.Text.Json.Serialization;

namespace PublishTool;

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(Config))]
public partial class JsonContext : JsonSerializerContext;