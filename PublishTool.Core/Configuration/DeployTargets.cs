namespace PublishTool.Configuration;

public sealed class TargetOverride
{
    public string? Path { get; set; }

    public string? ServiceName { get; set; }

    public bool ServiceSet { get; set; }
}

public sealed record TargetResolution(IReadOnlyDictionary<string, DeployTarget> Targets, IReadOnlyList<string> Missing);

public static class DeployTargets
{
    public static TargetResolution Resolve(
        ServerConfig config,
        string prefix,
        IReadOnlyList<string> projects,
        IReadOnlyDictionary<string, TargetOverride>? overrides = null)
    {
        var environment = config.FindEnvironment(prefix);
        var targets = new Dictionary<string, DeployTarget>(StringComparer.OrdinalIgnoreCase);
        var missing = new List<string>();
        foreach (string project in projects)
        {
            var stored = environment?.TargetFor(project);
            var change = FindOverride(overrides, project);
            string? path = change?.Path ?? stored?.Path;
            string? service = change is { ServiceSet: true } ? change.ServiceName : stored?.ServiceName;
            if (string.IsNullOrWhiteSpace(path))
            {
                missing.Add(project);
                continue;
            }

            targets[project] = new DeployTarget
            {
                Path = path,
                ServiceName = string.IsNullOrWhiteSpace(service) ? null : service.Trim()
            };
        }

        return new TargetResolution(targets, missing);
    }

    public static bool TryNormalizePath(string value, out string normalized)
    {
        string trimmed = value.Trim();
        if (!System.IO.Path.IsPathFullyQualified(trimmed))
        {
            normalized = trimmed;
            return false;
        }

        normalized = System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(trimmed));
        return true;
    }

    private static TargetOverride? FindOverride(IReadOnlyDictionary<string, TargetOverride>? overrides, string project) =>
        overrides?.FirstOrDefault(o => string.Equals(o.Key, project, StringComparison.OrdinalIgnoreCase)).Value;
}
