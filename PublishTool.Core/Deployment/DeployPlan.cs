using PublishTool.Configuration;

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
}

public sealed class DeployPlan
{
    private DeployPlan(DeployPackage package) => Package = package;

    public DeployPackage Package { get; }

    public List<TargetPlan> Targets { get; } = [];

    public List<DeployWarning> Warnings { get; } = [];

    public List<string> Errors { get; } = [];

    public bool IsEmpty => Targets.TrueForAll(t => t.Project.Files.Count == 0 && t.Delete.Count == 0);

    public static DeployPlan Create(
        DeployPackage package,
        IReadOnlyDictionary<string, DeployTarget> mappings,
        ServerConfig config,
        IHostController hosts,
        string toolVersion)
    {
        var plan = new DeployPlan(package);
        var manifest = package.Manifest;
        if (!string.Equals(manifest.ToolVersion, toolVersion, StringComparison.Ordinal))
        {
            plan.Warnings.Add(new DeployWarning(WarningCodes.ToolVersion,
                $"The package was created by PublishTool {manifest.ToolVersion}, but this is PublishTool {toolVersion}."));
        }

        foreach (var project in manifest.Projects)
        {
            var mapping = mappings[project.Name];
            string targetPath = PackagePaths.NormalizeDirectory(mapping.Path!);
            var target = new TargetPlan
            {
                Project = project,
                TargetPath = targetPath,
                ServiceName = string.IsNullOrWhiteSpace(mapping.ServiceName) ? null : mapping.ServiceName.Trim(),
                Current = config.RecordFor(targetPath)
            };
            plan.FillFiles(target);
            plan.CheckChain(target);
            plan.Targets.Add(target);
        }

        plan.CheckTargets(hosts);
        return plan;
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
            .Where(deleted => !files.Contains(deleted) && File.Exists(PackagePaths.Resolve(target.TargetPath, deleted))));
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

    private void CheckTargets(IHostController hosts)
    {
        foreach (var group in Targets.GroupBy(t => t.TargetPath, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
        {
            Errors.Add($"The projects {string.Join(", ", group.Select(t => t.Project.Name))} are all mapped to {group.Key}.");
        }

        if (Targets.Exists(t => t.IsIis) && !hosts.IsIisInstalled())
        {
            Errors.Add("Some targets are IIS apps, but IIS (the W3SVC service) is not installed on this machine.");
        }

        var missingServices = Targets
            .Select(t => t.ServiceName)
            .OfType<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(service => !hosts.ServiceExists(service));
        foreach (string service in missingServices)
        {
            Errors.Add($"The Windows service {service} does not exist on this machine.");
        }
    }

    private static bool IsInside(string directory, string root) =>
        directory.Length >= root.Length && directory.StartsWith(root, StringComparison.OrdinalIgnoreCase);
}
