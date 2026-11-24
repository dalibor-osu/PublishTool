namespace PublishTool.Deployment;

public static class PackagePaths
{
    public const string BackupDirectoryName = ".backup";

    public static string ToManifest(string relativePath) => relativePath.Replace('\\', '/');

    public static string Resolve(string root, string manifestPath)
    {
        string fullRoot = Path.GetFullPath(root);
        string prefix = Path.EndsInDirectorySeparator(fullRoot) ? fullRoot : fullRoot + Path.DirectorySeparatorChar;
        string native = manifestPath.Replace('/', Path.DirectorySeparatorChar);
        if (native.Length == 0 || Path.IsPathRooted(native))
        {
            throw new DeployException($"The path '{manifestPath}' in the package is not a relative file path.");
        }

        string full = Path.GetFullPath(Path.Combine(fullRoot, native));
        if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new DeployException($"The path '{manifestPath}' in the package points outside of {fullRoot}.");
        }

        return full;
    }

    public static string NormalizeDirectory(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
}
