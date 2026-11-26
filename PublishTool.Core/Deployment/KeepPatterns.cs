using System.IO.Enumeration;

namespace PublishTool.Deployment;

public sealed class KeepPatterns(IEnumerable<string> patterns)
{
    private const string AnyDepth = "**";

    private readonly List<string[]> _patterns = [.. patterns.Select(Split).Where(p => p.Length > 0)];

    public static bool TryNormalize(string value, out string pattern, out string? error)
    {
        pattern = value.Trim().Replace('\\', '/');
        if (pattern.StartsWith('/') || pattern.Contains(':'))
        {
            error = $"The keep pattern '{value}' must be relative to the target directory";
            return false;
        }

        pattern = pattern.TrimEnd('/');
        if (pattern.Length == 0)
        {
            error = "A keep pattern must not be empty";
            return false;
        }

        if (pattern.Split('/').Any(segment => segment.Trim() is "" or "." or ".."))
        {
            error = $"The keep pattern '{value}' must not contain empty, '.' or '..' segments";
            return false;
        }

        error = null;
        return true;
    }

    public bool IsKept(string manifestPath)
    {
        string[] path = manifestPath.Split('/');
        return Enumerable.Range(1, path.Length).Any(length => _patterns.Exists(pattern => Matches(pattern, 0, path, 0, length)));
    }

    private static string[] Split(string pattern)
    {
        string[] segments = pattern.Trim().Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return segments.Length == 1 && segments[0] != AnyDepth ? [AnyDepth, segments[0]] : segments;
    }

    private static bool Matches(string[] pattern, int patternIndex, string[] path, int pathIndex, int pathLength)
    {
        if (patternIndex == pattern.Length)
        {
            return pathIndex == pathLength;
        }

        if (pattern[patternIndex] == AnyDepth)
        {
            return Matches(pattern, patternIndex + 1, path, pathIndex, pathLength)
                || (pathIndex < pathLength && Matches(pattern, patternIndex, path, pathIndex + 1, pathLength));
        }

        return pathIndex < pathLength
            && FileSystemName.MatchesSimpleExpression(pattern[patternIndex], path[pathIndex], ignoreCase: true)
            && Matches(pattern, patternIndex + 1, path, pathIndex + 1, pathLength);
    }
}
