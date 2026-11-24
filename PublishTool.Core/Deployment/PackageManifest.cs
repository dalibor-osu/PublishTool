namespace PublishTool.Deployment;

public sealed class PackageManifest
{
    public const int CurrentFormat = 1;
    public const string FileName = "publishtool-manifest.json";

    public int Format { get; set; } = CurrentFormat;

    public string ToolVersion { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Prefix { get; set; } = string.Empty;

    public DateTimeOffset Created { get; set; }

    public string? PreviousReference { get; set; }

    public List<ManifestProject> Projects { get; set; } = [];
}

public sealed class ManifestProject
{
    public string Name { get; set; } = string.Empty;

    public bool IsFullPublish { get; set; }

    public string? Base { get; set; }

    public List<ManifestFile> Files { get; set; } = [];

    public List<ManifestDeletion> Deleted { get; set; } = [];
}

public sealed class ManifestFile
{
    public string Path { get; set; } = string.Empty;

    public long Size { get; set; }

    public string Sha256 { get; set; } = string.Empty;

    public string? PreviousSha256 { get; set; }
}

public sealed class ManifestDeletion
{
    public string Path { get; set; } = string.Empty;

    public string Sha256 { get; set; } = string.Empty;
}

public sealed class FormatEnvelope
{
    public int? Format { get; set; }
}
