namespace PublishTool;

public static class MsBuildProperties
{
    public const string Prefix = "PublishToolPrefix";

    private const string AllowedPunctuation = ".-_+";

    public static string? PrefixProblem(string prefix)
    {
        if (prefix.Length == 0)
        {
            return $"The package prefix is empty, so it cannot be passed to MSBuild as {Prefix}.";
        }

        int invalid = Array.FindIndex(prefix.ToCharArray(), c => !IsAllowed(c));
        return invalid < 0
            ? null
            : $"The package prefix '{prefix}' (the git branch with '/' replaced by '-') contains '{prefix[invalid]}', which cannot be passed to "
              + $"MSBuild as {Prefix} safely. Publish from a branch whose name consists of letters, digits, '/', '.', '_', '-' and '+'.";
    }

    private static bool IsAllowed(char c) => char.IsLetterOrDigit(c) || AllowedPunctuation.Contains(c);
}
