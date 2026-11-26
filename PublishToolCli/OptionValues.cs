using PublishTool.Configuration;
using PublishTool.Deployment;
using PublishTool.Helpers;

namespace PublishToolCli;

public static class OptionValues
{
    public static string[] SplitList(string value) =>
        [.. value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];

    public static bool TryResolvePath(string value, out string resolved, out string? error)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            resolved = value;
            error = "The path must not be empty";
            return false;
        }

        if (!OptionParsers.TryResolveDirectory(value.Trim(), Directory.GetCurrentDirectory(), out resolved, out string? resolveError))
        {
            error = resolveError;
            return false;
        }

        error = null;
        return true;
    }

    public static bool TryAddCodes(string value, List<string> codes, out string? error)
    {
        foreach (string code in SplitList(value))
        {
            if (!WarningCodes.IsKnown(code))
            {
                error = $"Unknown warning code '{code}'. Known codes: {string.Join(", ", WarningCodes.All)}";
                return false;
            }

            codes.Add(code.ToLowerInvariant());
        }

        error = null;
        return true;
    }

    public static bool TryAddProjects(string value, string option, List<string> projects, out string? error)
    {
        string[] names = SplitList(value);
        projects.AddRange(names);
        error = names.Length == 0 ? $"{option} needs a project name" : null;
        return error is null;
    }

    public static bool TryAddTarget(string value, Dictionary<string, TargetOverride> overrides, out string? error)
    {
        if (!TrySplitPair(value, "--target", "<project>=<absolute path>", out string project, out string path, out error))
        {
            return false;
        }

        if (!DeployTargets.TryNormalizePath(path, out string normalized))
        {
            error = $"The target of {project} must be an absolute path, got '{path}'";
            return false;
        }

        OverrideFor(overrides, project).Path = normalized;
        return true;
    }

    public static bool TryAddService(string value, Dictionary<string, TargetOverride> overrides, out string? error)
    {
        if (!TrySplitPair(value, "--service", "<project>=<service name>", out string project, out string service, out error))
        {
            return false;
        }

        var target = OverrideFor(overrides, project);
        target.ServiceSet = true;
        target.ServiceName = service.Length == 0 ? null : service;
        return true;
    }

    public static bool TryAddKeep(string value, Dictionary<string, TargetOverride> overrides, out string? error)
    {
        if (!TrySplitKeep(value, "--keep", out string project, out string pattern, out error))
        {
            return false;
        }

        var target = OverrideFor(overrides, project);
        if (!target.Keep.Contains(pattern, StringComparer.OrdinalIgnoreCase))
        {
            target.Keep.Add(pattern);
        }

        return true;
    }

    public static bool TryAddRemovedKeep(string value, List<KeyValuePair<string, string>> removed, out string? error)
    {
        if (!TrySplitKeep(value, "--remove-keep", out string project, out string pattern, out error))
        {
            return false;
        }

        removed.Add(new KeyValuePair<string, string>(project, pattern));
        return true;
    }

    private static bool TrySplitKeep(string value, string option, out string project, out string pattern, out string? error)
    {
        pattern = string.Empty;
        return TrySplitPair(value, option, "<project>=<pattern>", out project, out string raw, out error)
            && KeepPatterns.TryNormalize(raw, out pattern, out error);
    }

    private static TargetOverride OverrideFor(Dictionary<string, TargetOverride> overrides, string project)
    {
        string key = ServerConfig.FindKey(overrides, project) ?? project;
        if (!overrides.TryGetValue(key, out var target))
        {
            target = new TargetOverride();
            overrides[key] = target;
        }

        return target;
    }

    private static bool TrySplitPair(string value, string option, string format, out string key, out string pairValue, out string? error)
    {
        int separator = value.IndexOf('=');
        key = separator < 0 ? string.Empty : value[..separator].Trim();
        pairValue = separator < 0 ? string.Empty : value[(separator + 1)..].Trim();
        if (key.Length == 0)
        {
            error = $"{option} expects {format}, got '{value}'";
            return false;
        }

        error = null;
        return true;
    }
}
