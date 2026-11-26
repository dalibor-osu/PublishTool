using System.Text.Json.Serialization;
using PublishTool.Configuration.Versions;
using PublishTool.Deployment;

namespace PublishTool.Configuration;

[JsonSourceGenerationOptions(WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ConfigEnvelope))]
[JsonSerializable(typeof(ConfigV1))]
[JsonSerializable(typeof(Config))]
[JsonSerializable(typeof(ServerConfig))]
[JsonSerializable(typeof(FormatEnvelope))]
[JsonSerializable(typeof(PackageManifest))]
[JsonSerializable(typeof(ReferenceLineage))]
[JsonSerializable(typeof(PackageInventory))]
[JsonSerializable(typeof(DeployJournal))]
public partial class JsonContext : JsonSerializerContext;
