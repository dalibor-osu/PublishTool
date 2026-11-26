namespace PublishTool.Deployment;

public sealed class PackageInventory
{
    public const int CurrentFormat = 1;
    public const string FileName = "publishtool-inventory.json";

    public int Format { get; set; } = CurrentFormat;

    public string Package { get; set; } = string.Empty;

    public List<InventoryProject> Projects { get; set; } = [];

    public InventoryProject? Find(string project) =>
        Projects.Find(p => string.Equals(p.Name, project, StringComparison.OrdinalIgnoreCase));
}

public sealed class InventoryProject
{
    public string Name { get; set; } = string.Empty;

    public List<InventoryFile> Files { get; set; } = [];
}

public sealed class InventoryFile
{
    public string Path { get; set; } = string.Empty;

    public long Size { get; set; }

    public string Sha256 { get; set; } = string.Empty;
}
