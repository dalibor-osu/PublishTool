using PublishTool.Helpers;

namespace PublishTool.Deployment;

public sealed class DeployPackage
{
    private DeployPackage(string directory, PackageManifest manifest, IReadOnlyList<string>? skippedProjects = null)
    {
        Directory = directory;
        Manifest = manifest;
        SkippedProjects = skippedProjects ?? [];
    }

    public string Directory { get; }

    public PackageManifest Manifest { get; }

    public IReadOnlyList<string> SkippedProjects { get; }

    public string BackupDirectory => BackupDirectoryOf(Directory);

    public string JournalPath => JournalPathOf(Directory);

    public int FileCount => Manifest.Projects.Sum(p => p.Files.Count);

    public string InventoryPath => Path.Combine(Directory, PackageInventory.FileName);

    public bool HasInventory => File.Exists(InventoryPath);

    public PackageInventory LoadInventory()
    {
        if (!HasInventory)
        {
            throw new DeployException(
                $"{InventoryPath} was not found, so the targets cannot be cleaned. The package was created by a PublishTool version "
                + "that did not write it yet, or the file was not copied to the server.");
        }

        var inventory = PackageJson.ReadInventory(InventoryPath);
        if (!string.Equals(inventory.Package, Manifest.Name, StringComparison.OrdinalIgnoreCase))
        {
            throw new DeployException(
                $"{InventoryPath} belongs to the package {inventory.Package}, not to {Manifest.Name}, so the targets cannot be cleaned.");
        }

        return inventory;
    }

    public static string BackupDirectoryOf(string packageDirectory) => Path.Combine(packageDirectory, PackagePaths.BackupDirectoryName);

    public static string JournalPathOf(string packageDirectory) => Path.Combine(BackupDirectoryOf(packageDirectory), DeployJournal.FileName);

    public static DeployPackage Load(string directory)
    {
        var manifest = PackageJson.ReadManifest(Path.Combine(directory, PackageManifest.FileName));
        if (manifest.Name.Length == 0 || manifest.Prefix.Length == 0)
        {
            throw new DeployException($"The manifest in {directory} has no package name or prefix.");
        }

        var package = new DeployPackage(directory, manifest);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var project in manifest.Projects)
        {
            _ = package.ProjectDirectory(project);
            if (!names.Add(project.Name))
            {
                throw new DeployException($"The manifest in {directory} lists the project {project.Name} more than once.");
            }
        }

        return package;
    }

    public DeployPackage WithoutProjects(IReadOnlyCollection<string> skipped)
    {
        if (skipped.Count == 0)
        {
            return this;
        }

        bool IsSkipped(ManifestProject project) => skipped.Contains(project.Name, StringComparer.OrdinalIgnoreCase);
        var manifest = new PackageManifest
        {
            Format = Manifest.Format,
            ToolVersion = Manifest.ToolVersion,
            Name = Manifest.Name,
            Prefix = Manifest.Prefix,
            Created = Manifest.Created,
            PreviousReference = Manifest.PreviousReference,
            Projects = [.. Manifest.Projects.Where(p => !IsSkipped(p))]
        };
        return new DeployPackage(Directory, manifest, [.. SkippedProjects, .. Manifest.Projects.Where(IsSkipped).Select(p => p.Name)]);
    }

    public string ProjectDirectory(ManifestProject project)
    {
        string path = PackagePaths.Resolve(Directory, project.Name);
        if (Path.GetDirectoryName(path) is not { } parent
            || !string.Equals(PackagePaths.NormalizeDirectory(parent), PackagePaths.NormalizeDirectory(Directory), StringComparison.OrdinalIgnoreCase))
        {
            throw new DeployException($"The project name '{project.Name}' in the manifest is not a plain directory name.");
        }

        return path;
    }

    public string FilePath(ManifestProject project, string manifestPath) => PackagePaths.Resolve(ProjectDirectory(project), manifestPath);

    public PackageVerification Verify(Action<string> report, CancellationToken ct)
    {
        var errors = new List<string>();
        var listed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int checkedFiles = 0;
        int total = FileCount;
        foreach (var project in Manifest.Projects)
        {
            foreach (var file in project.Files)
            {
                ct.ThrowIfCancellationRequested();
                string path = FilePath(project, file.Path);
                listed.Add(path);
                if (VerifyFile(path, file) is { } error)
                {
                    errors.Add($"{project.Name}/{file.Path}: {error}");
                }

                checkedFiles++;
                if (checkedFiles % 25 == 0 || checkedFiles == total)
                {
                    report($"{checkedFiles}/{total} files verified");
                }
            }
        }

        return new PackageVerification(errors, FindExtraFiles(listed));
    }

    private static string? VerifyFile(string path, ManifestFile file)
    {
        var info = new FileInfo(path);
        if (!info.Exists)
        {
            return "missing";
        }

        if (info.Length != file.Size)
        {
            return $"size is {info.Length} bytes, the manifest says {file.Size}";
        }

        return string.Equals(FileHashing.Sha256(path), file.Sha256, StringComparison.OrdinalIgnoreCase)
            ? null
            : "content differs from the manifest (SHA-256 mismatch)";
    }

    private List<string> FindExtraFiles(HashSet<string> listed)
    {
        var extra = new List<string>();
        foreach (string directory in System.IO.Directory.EnumerateDirectories(Directory))
        {
            string name = Path.GetFileName(directory);
            if (string.Equals(name, PackagePaths.BackupDirectoryName, StringComparison.OrdinalIgnoreCase)
                || SkippedProjects.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            extra.AddRange(System.IO.Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
                .Where(file => !listed.Contains(Path.GetFullPath(file)))
                .Select(file => PackagePaths.ToManifest(Path.GetRelativePath(Directory, file))));
        }

        return extra;
    }
}

public sealed record PackageVerification(IReadOnlyList<string> Errors, IReadOnlyList<string> ExtraFiles);
