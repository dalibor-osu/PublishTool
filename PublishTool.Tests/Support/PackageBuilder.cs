using PublishTool.Deployment;
using PublishTool.Helpers;

namespace PublishTool.Tests.Support;

public sealed class PackageBuilder(TempDirectory root, string name = "uat_2026-01-01_12-00-00", string prefix = "uat")
{
    private readonly List<ManifestProject> _projects = [];

    public string Directory => root.Combine("packages", name);

    public string ToolVersion { get; set; } = BuildInfo.Version;

    public PackageBuilder Project(string project, string? basePackage, bool isFullPublish, IReadOnlyDictionary<string, string> files,
        IReadOnlyList<string>? deleted = null)
    {
        var manifestProject = new ManifestProject { Name = project, Base = basePackage, IsFullPublish = isFullPublish, Deleted = [.. deleted ?? []] };
        foreach (var (path, content) in files)
        {
            string file = root.Write($"packages/{name}/{project}/{path}", content);
            manifestProject.Files.Add(new ManifestFile { Path = path, Size = new FileInfo(file).Length, Sha256 = FileHashing.Sha256(file) });
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
        return DeployPackage.Load(Directory);
    }
}
