using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using PublishTool.Configuration;

namespace PublishTool.Deployment;

public static class PackageJson
{
    public static PackageManifest ReadManifest(string path)
    {
        if (!File.Exists(path))
        {
            throw new DeployException(
                $"{path} was not found. Packages without a manifest (made by an older PublishTool or put together by hand) are not "
                + "deployed; publish again with the current PublishTool.");
        }

        return Read(path, PackageManifest.CurrentFormat, "package manifest", JsonContext.Default.PackageManifest);
    }

    public static void WriteManifest(string path, PackageManifest manifest) => Write(path, manifest, JsonContext.Default.PackageManifest);

    public static DeployJournal ReadJournal(string path) => Read(path, DeployJournal.CurrentFormat, "deploy journal", JsonContext.Default.DeployJournal);

    public static void WriteJournal(string path, DeployJournal journal) => Write(path, journal, JsonContext.Default.DeployJournal);

    public static ReferenceLineage? TryReadLineage(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return Read(path, ReferenceLineage.CurrentFormat, "reference lineage", JsonContext.Default.ReferenceLineage);
        }
        catch (DeployException e)
        {
            Logger.LogWarning($"The lineage of the previous reference is ignored: {e.Message}");
            return null;
        }
    }

    public static void WriteLineage(Stream stream, ReferenceLineage lineage) =>
        JsonSerializer.Serialize(stream, lineage, JsonContext.Default.ReferenceLineage);

    public static PackageInventory ReadInventory(string path) =>
        Read(path, PackageInventory.CurrentFormat, "package inventory", JsonContext.Default.PackageInventory);

    public static PackageInventory? TryReadInventory(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return ReadInventory(path);
        }
        catch (DeployException e)
        {
            Logger.LogWarning($"The inventory of the previous reference is ignored: {e.Message}");
            return null;
        }
    }

    public static void WriteInventory(string path, PackageInventory inventory) => Write(path, inventory, JsonContext.Default.PackageInventory);

    public static void WriteInventory(Stream stream, PackageInventory inventory) =>
        JsonSerializer.Serialize(stream, inventory, JsonContext.Default.PackageInventory);

    private static T Read<T>(string path, int currentFormat, string kind, JsonTypeInfo<T> typeInfo) where T : class
    {
        try
        {
            string json = File.ReadAllText(path);
            int format = JsonSerializer.Deserialize(json, JsonContext.Default.FormatEnvelope)?.Format ?? 0;
            if (format > currentFormat)
            {
                throw new DeployException(
                    $"The {kind} {path} has format version {format}, but this PublishTool only understands versions up to "
                    + $"{currentFormat}. Update PublishTool.");
            }

            if (format < 1)
            {
                throw new DeployException($"The {kind} {path} has no valid format version.");
            }

            return JsonSerializer.Deserialize(json, typeInfo) ?? throw new DeployException($"The {kind} {path} is empty.");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            throw new DeployException($"The {kind} {path} could not be read: {e.Message}", e);
        }
    }

    private static void Write<T>(string path, T value, JsonTypeInfo<T> typeInfo)
    {
        string temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(value, typeInfo));
        File.Move(temporary, path, overwrite: true);
    }
}
