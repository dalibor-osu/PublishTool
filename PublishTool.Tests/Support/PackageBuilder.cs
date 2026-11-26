using System.Security.Cryptography;
using System.Text;
using PublishTool.Deployment;
using PublishTool.Helpers;

namespace PublishTool.Tests.Support;

public sealed class PackageBuilder(TempDirectory root, string name = "uat_2026-01-01_12-00-00", string prefix = "uat")
{
    private readonly List<ManifestProject> _projects = [];
    private readonly List<InventoryProject> _inventory = [];

    public string? InventoryPackage { get; set; }

    public PackageBuilder Inventory(string project, IReadOnlyDictionary<string, string> files)
    {
        _inventory.Add(new InventoryProject
        {
            Name = project,
            Files = [.. files.Select(f => new InventoryFile { Path = f.Key, Size = Encoding.UTF8.GetByteCount(f.Value), Sha256 = Sha256Of(f.Value) })]
        });
        return this;
    }

    public string Directory => root.Combine("packages", name);

    public string ToolVersion { get; set; } = BuildInfo.Version;

    public static string Sha256Of(string content) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(content)));

    public PackageBuilder Project(string project, string? basePackage, bool isFullPublish, IReadOnlyDictionary<string, string> files,
        IReadOnlyDictionary<string, string>? deleted = null, IReadOnlyDictionary<string, string>? previous = null)
    {
        var manifestProject = new ManifestProject
        {
            Name = project,
            Base = basePackage,
            IsFullPublish = isFullPublish,
            Deleted = [.. (deleted ?? new Dictionary<string, string>()).Select(d => new ManifestDeletion { Path = d.Key, Sha256 = Sha256Of(d.Value) })]
        };
        foreach (var (path, content) in files)
        {
            string file = root.Write($"packages/{name}/{project}/{path}", content);
            manifestProject.Files.Add(new ManifestFile
            {
                Path = path,
                Size = new FileInfo(file).Length,
                Sha256 = FileHashing.Sha256(file),
                PreviousSha256 = previous is not null && previous.TryGetValue(path, out string? old) ? Sha256Of(old) : null
            });
        }

        _projects.Add(manifestProject);
        return this;
    }

    public DeployPackage Build()
    {
        System.IO.Directory.CreateDirectory(Directory);
        PackageJson.WriteManifest(Path.Combine(Directory, PackageManifest.FileName), new PackageManifest
        {
            ToolVersion = ToolVersion,
            Name = name,
            Prefix = prefix,
            Created = DateTimeOffset.Now,
            Projects = _projects
        });
        if (_inventory.Count > 0)
        {
            PackageJson.WriteInventory(Path.Combine(Directory, PackageInventory.FileName),
                new PackageInventory { Package = InventoryPackage ?? name, Projects = _inventory });
        }

        return DeployPackage.Load(Directory);
    }
}
