using PublishTool.Configuration;
using PublishTool.Helpers;

namespace PublishTool.Deployment;

public sealed class TargetPlan
{
    public required ManifestProject Project { get; init; }

    public required string TargetPath { get; init; }

    public string? ServiceName { get; init; }

    public bool IsIis => ServiceName is null;

    public DeployRecord? Current { get; init; }

    public List<string> Overwrite { get; } = [];

    public List<string> Create { get; } = [];

    public List<string> Delete { get; } = [];

    public List<string> CreatedDirectories { get; } = [];

    public bool IsCleanOnly { get; init; }

    public List<string> Stray { get; } = [];

    public List<string> EmptyDirectories { get; } = [];

    public bool HasChanges => Project.Files.Count > 0 || Delete.Count > 0 || Stray.Count > 0 || EmptyDirectories.Count > 0;
}

public sealed class DeployPlan
{
    private readonly ServerConfig _config;
    private readonly IHostController _hosts;

    private DeployPlan(DeployPackage package, ServerConfig config, IHostController hosts)
    {
        Package = package;
        _config = config;
        _hosts = hosts;
    }

    public DeployPackage Package { get; }

    public List<TargetPlan> Targets { get; } = [];

    public List<DeployWarning> Warnings { get; } = [];

    public List<string> Errors { get; } = [];

    public bool IsCleaned { get; private set; }

    public List<string> ProtectedConfigFiles { get; } = [];

    public List<string> NotCleaned { get; } = [];

    public bool IsEmpty => Targets.TrueForAll(t => !t.HasChanges);

    public bool IsAllSkipped => Targets.Count == 0 && Package.SkippedProjects.Count > 0;

    public static DeployPlan Create(
        DeployPackage package,
        IReadOnlyDictionary<string, DeployTarget> mappings,
        ServerConfig config,
        IHostController hosts,
        string toolVersion,
        CancellationToken ct)
    {
        var plan = new DeployPlan(package, config, hosts);
        var manifest = package.Manifest;
        if (!string.Equals(manifest.ToolVersion, toolVersion, StringComparison.Ordinal))
        {
            plan.Warnings.Add(new DeployWarning(WarningCodes.ToolVersion,
                $"The package was created by PublishTool {manifest.ToolVersion}, but this is PublishTool {toolVersion}."));
        }

        foreach (var project in manifest.Projects)
        {
            var target = plan.NewTarget(project, mappings[project.Name], cleanOnly: false);
            plan.FillFiles(target);
            plan.CheckChain(target);
            plan.CheckConfigFiles(target);
            plan.CheckTargetContent(target, ct);
            plan.Targets.Add(target);
        }

        plan.CheckTargets();
        return plan;
    }

    public void Clean(
        PackageInventory inventory,
        IReadOnlyDictionary<string, TargetOverride>? overrides,
        IReadOnlyCollection<string>? skip,
        CancellationToken ct)
    {
        ResetClean();
        IsCleaned = true;
        if (Targets.Find(t => inventory.Find(t.Project.Name) is null) is { } unlisted)
        {
            throw new DeployException(
                $"{PackageInventory.FileName} does not list the project {unlisted.Project.Name} of the package, so the targets cannot be cleaned.");
        }

        string[] projects =
        [
            .. inventory.Projects.Select(p => p.Name).Where(p => !Package.SkippedProjects.Contains(p, StringComparer.OrdinalIgnoreCase))
        ];
        var resolution = DeployTargets.Resolve(_config, Package.Manifest.Prefix, projects, overrides, skip);
        NotCleaned.AddRange(resolution.Missing);
        string[] protectedDirectories = [.. ProtectedDirectories(resolution)];
        foreach (var listed in inventory.Projects)
        {
            if (!resolution.Targets.TryGetValue(listed.Name, out var mapping))
            {
                continue;
            }

            var target = FindTarget(listed.Name) ?? NewTarget(new ManifestProject { Name = listed.Name }, mapping, cleanOnly: true);
            var findings = TargetCleaner.Scan(target, listed, mapping.Keep, protectedDirectories, ct);
            target.Stray.AddRange(findings.Stray);
            target.EmptyDirectories.AddRange(findings.EmptyDirectories);
            ProtectedConfigFiles.AddRange(findings.ProtectedConfigFiles.Select(file => $"{listed.Name}/{file}"));
            AddCleanWarnings(target, findings);
            if (target.IsCleanOnly && target.HasChanges)
            {
                Targets.Add(target);
            }
        }

        Errors.Clear();
        CheckTargets();
    }

    public void ResetClean()
    {
        IsCleaned = false;
        Targets.RemoveAll(t => t.IsCleanOnly);
        foreach (var target in Targets)
        {
            target.Stray.Clear();
            target.EmptyDirectories.Clear();
        }

        Warnings.RemoveAll(w => WarningCodes.IsClean(w.Code));
        ProtectedConfigFiles.Clear();
        NotCleaned.Clear();
        Errors.Clear();
        CheckTargets();
    }

    private TargetPlan? FindTarget(string project) =>
        Targets.Find(t => string.Equals(t.Project.Name, project, StringComparison.OrdinalIgnoreCase));

    private TargetPlan NewTarget(ManifestProject project, DeployTarget mapping, bool cleanOnly)
    {
        string targetPath = PackagePaths.NormalizeDirectory(mapping.Path!);
        return new TargetPlan
        {
            Project = project,
            TargetPath = targetPath,
            ServiceName = string.IsNullOrWhiteSpace(mapping.ServiceName) ? null : mapping.ServiceName.Trim(),
            Current = _config.RecordFor(targetPath),
            IsCleanOnly = cleanOnly
        };
    }

    private IEnumerable<string> ProtectedDirectories(TargetResolution resolution) =>
        _config.Environments.Values
            .SelectMany(environment => environment.Targets.Values)
            .Concat(resolution.Targets.Values)
            .Select(target => target.Path)
            .OfType<string>()
            .Where(path => Path.IsPathFullyQualified(path))
            .Concat(Targets.Select(target => target.TargetPath))
            .Append(Package.Directory);

    private void AddCleanWarnings(TargetPlan target, CleanFindings findings)
    {
        string name = target.Project.Name;
        if (findings.Stray.Count + findings.EmptyDirectories.Count > 0)
        {
            List<string> files = [.. findings.Stray, .. findings.EmptyDirectories.Select(directory => $"{directory}/")];
            Warnings.Add(new DeployWarning(WarningCodes.CleanDelete,
                $"{name}: cleaning {target.TargetPath} deletes {findings.Stray.Count} file(s) that are not part of the publish and removes "
                + $"{findings.EmptyDirectories.Count} empty directories: {DeployWarning.ListFiles(files)}.",
                files));
        }

        if (findings.Mismatched.Count + findings.Unreadable.Count > 0)
        {
            List<string> files = [.. findings.Mismatched, .. findings.Unreadable];
            List<string> listed = [.. findings.Mismatched, .. findings.Unreadable.Select(file => $"{file} (could not be read)")];
            Warnings.Add(new DeployWarning(WarningCodes.CleanMismatch,
                $"{name}: {files.Count} file(s) in {target.TargetPath} differ from the publish; the package does not contain them, so they stay "
                + $"as they are: {DeployWarning.ListFiles(listed)}.",
                files));
        }

        if (findings.Missing.Count > 0)
        {
            Warnings.Add(new DeployWarning(WarningCodes.CleanMissing,
                $"{name}: {findings.Missing.Count} file(s) of the publish are missing in {target.TargetPath} and the package does not contain "
                + $"them: {DeployWarning.ListFiles(findings.Missing)}.",
                findings.Missing));
        }
    }

    private void FillFiles(TargetPlan target)
    {
        if (!Directory.Exists(target.TargetPath))
        {
            Warnings.Add(new DeployWarning(WarningCodes.TargetMissing,
                $"{target.Project.Name}: the target directory {target.TargetPath} does not exist and will be created."));
        }

        var created = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string file in target.Project.Files.Select(f => f.Path))
        {
            files.Add(file);
            string path = PackagePaths.Resolve(target.TargetPath, file);
            if (File.Exists(path))
            {
                target.Overwrite.Add(file);
                continue;
            }

            target.Create.Add(file);
            for (string? directory = Path.GetDirectoryName(path);
                 directory is not null && IsInside(directory, target.TargetPath) && !Directory.Exists(directory);
                 directory = Path.GetDirectoryName(directory))
            {
                created.Add(directory);
            }
        }

        target.CreatedDirectories.AddRange(created.OrderByDescending(d => d.Length));
        target.Delete.AddRange(target.Project.Deleted
            .Select(deleted => deleted.Path)
            .Where(deleted => !files.Contains(deleted) && File.Exists(PackagePaths.Resolve(target.TargetPath, deleted))));
    }

    private void CheckConfigFiles(TargetPlan target)
    {
        List<string> files = [.. target.Overwrite.Where(ConfigFiles.IsConfigFile), .. target.Delete.Where(ConfigFiles.IsConfigFile)];
        if (files.Count == 0)
        {
            return;
        }

        var deleted = target.Delete.ToHashSet(StringComparer.OrdinalIgnoreCase);
        List<string> listed = [.. files.Select(f => deleted.Contains(f) ? $"{f} (deleted)" : f)];
        Warnings.Add(new DeployWarning(WarningCodes.ConfigChanged,
            $"{target.Project.Name}: the package overwrites or deletes {files.Count} config file(s) in {target.TargetPath}: "
            + $"{DeployWarning.ListFiles(listed)}.",
            files));
    }

    private void CheckTargetContent(TargetPlan target, CancellationToken ct)
    {
        var packageFiles = target.Project.Files
            .DistinctBy(f => f.Path, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(f => f.Path, StringComparer.OrdinalIgnoreCase);
        var deletions = target.Project.Deleted
            .DistinctBy(d => d.Path, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(d => d.Path, StringComparer.OrdinalIgnoreCase);
        var expected = target.Overwrite
            .Select(path => (Path: path, Hashes: new[] { packageFiles[path].Sha256, packageFiles[path].PreviousSha256 }))
            .Concat(target.Delete.Select(path => (Path: path, Hashes: new string?[] { deletions[path].Sha256 })));

        var modified = new List<string>();
        var unreadable = new List<string>();
        foreach (var (path, hashes) in expected)
        {
            ct.ThrowIfCancellationRequested();
            switch (HasExpectedContent(PackagePaths.Resolve(target.TargetPath, path), hashes))
            {
                case false:
                    modified.Add(path);
                    break;
                case null:
                    unreadable.Add(path);
                    break;
            }
        }

        if (modified.Count + unreadable.Count > 0)
        {
            AddTargetModified(target, modified, unreadable);
        }
    }

    private void AddTargetModified(TargetPlan target, List<string> modified, List<string> unreadable)
    {
        List<string> files = [.. modified, .. unreadable];
        List<string> listed = [.. modified, .. unreadable.Select(f => $"{f} (could not be read)")];
        Warnings.Add(new DeployWarning(WarningCodes.TargetModified,
            $"{target.Project.Name}: {files.Count} file(s) in {target.TargetPath} are not what this package expects to overwrite or delete, "
            + "so they were probably changed on the server or by a package that was skipped; the package replaces them: "
            + $"{DeployWarning.ListFiles(listed)}.",
            files));
    }

    private static bool? HasExpectedContent(string path, string?[] hashes)
    {
        try
        {
            string actual = FileHashing.Sha256(path);
            return hashes.Any(h => string.Equals(h, actual, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Logger.LogWarning($"Could not read {path} to compare it with the package:\n{e}");
            return null;
        }
    }

    private void CheckChain(TargetPlan target)
    {
        string name = target.Project.Name;
        string? current = target.Current?.Package;
        string? expected = target.Project.Base;
        if (string.Equals(current, Package.Manifest.Name, StringComparison.OrdinalIgnoreCase))
        {
            Warnings.Add(new DeployWarning(WarningCodes.AlreadyDeployed, $"{name}: this package was already deployed to {target.TargetPath}."));
        }
        else if (target.Project.IsFullPublish)
        {
            if (current is not null)
            {
                Warnings.Add(new DeployWarning(WarningCodes.FullPublish,
                    $"{name}: the package holds a full publish, but {target.TargetPath} was deployed with {current}. "
                    + "Files deleted since then are not removed."));
            }
        }
        else if (expected is null)
        {
            Warnings.Add(new DeployWarning(WarningCodes.UnknownBase,
                $"{name}: the package does not know which package it is based on (the previous reference has no lineage), "
                + $"so the deploy order cannot be checked; {target.TargetPath} is at {current ?? "no recorded deploy"}."));
        }
        else if (current is null)
        {
            Warnings.Add(new DeployWarning(WarningCodes.NoRecord,
                $"{name}: {target.TargetPath} has no recorded deploy, but this package is based on {expected}."));
        }
        else if (!string.Equals(current, expected, StringComparison.OrdinalIgnoreCase))
        {
            Warnings.Add(new DeployWarning(WarningCodes.ChainMismatch,
                $"{name}: {target.TargetPath} is at {current}, but this package is based on {expected}. "
                + "A package was skipped or is deployed out of order."));
        }
    }

    private void CheckTargets()
    {
        foreach (var group in Targets.GroupBy(t => t.TargetPath, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
        {
            Errors.Add($"The projects {string.Join(", ", group.Select(t => t.Project.Name))} are all mapped to {group.Key}.");
        }

        if (Targets.Exists(t => t.IsIis) && !_hosts.IsIisInstalled())
        {
            Errors.Add("Some targets are IIS apps, but IIS (the W3SVC service) is not installed on this machine.");
        }

        var missingServices = Targets
            .Select(t => t.ServiceName)
            .OfType<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(service => !_hosts.ServiceExists(service));
        foreach (string service in missingServices)
        {
            Errors.Add($"The Windows service {service} does not exist on this machine.");
        }
    }

    private static bool IsInside(string directory, string root) =>
        directory.Length >= root.Length && directory.StartsWith(root, StringComparison.OrdinalIgnoreCase);
}
