using System.Diagnostics.CodeAnalysis;

namespace PublishTool.Helpers;

internal static class OptionParsers
{
    public static bool TryResolveDirectory(string value, string currentDirectory, out string resolved, [NotNullWhen(false)] out string? error)
    {
        try
        {
            resolved = Path.GetFullPath(Path.GetRelativePath(currentDirectory, value));
            error = null;
            return true;
        }
        catch (Exception e)
        {
            Logger.LogError(e.ToString());
            resolved = currentDirectory;

            error = "An error occurred while resolving the directory";
            return false;
        }
    }
}