using PublishTool.Helpers;

namespace PublishTool.Deployment;

public sealed class CleanFindings
{
    public List<string> Stray { get; } = [];

    public List<string> EmptyDirectories { get; } = [];

    public List<string> ProtectedConfigFiles { get; } = [];

    public List<string> Mismatched { get; } = [];

    public List<string> Unreadable { get; } = [];

    public List<string> Missing { get; } = [];
}

public sealed class TargetCleaner
{
    private readonly string _root;
    private readonly Dictionary<string, InventoryFile> _expected = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _expectedDirectories = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _packageFiles;
    private readonly HashSet<string> _packageDeletes;
    private readonly HashSet<string> _protectedDirectories;
    private readonly KeepPatterns _keep;
    private readonly HashSet<string> _seen = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _skippedDirectories = [];
    private readonly CleanFindings _findings = new();
    private readonly CancellationToken _ct;

    private TargetCleaner(
        TargetPlan target, InventoryProject inventory, IEnumerable<string> keep, IEnumerable<string> protectedDirectories, CancellationToken ct)
    {
        _root = target.TargetPath;
        _packageFiles = target.Project.Files.Select(f => f.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        _packageDeletes = target.Delete.ToHashSet(StringComparer.OrdinalIgnoreCase);
        _protectedDirectories = protectedDirectories.Select(PackagePaths.NormalizeDirectory).ToHashSet(StringComparer.OrdinalIgnoreCase);
        _keep = new KeepPatterns(keep);
        _ct = ct;
        foreach (var file in inventory.Files)
        {
            _expected[file.Path] = file;
            for (int separator = file.Path.IndexOf('/'); separator >= 0; separator = file.Path.IndexOf('/', separator + 1))
            {
                _expectedDirectories.Add(file.Path[..separator]);
            }
        }
    }

    public static CleanFindings Scan(
        TargetPlan target, InventoryProject inventory, IEnumerable<string> keep, IEnumerable<string> protectedDirectories, CancellationToken ct) =>
        new TargetCleaner(target, inventory, keep, protectedDirectories, ct).Run();

    private CleanFindings Run()
    {
        if (Directory.Exists(_root))
        {
            ScanDirectory(new DirectoryInfo(_root), string.Empty);
        }

        foreach (string path in _expected.Keys)
        {
            _ct.ThrowIfCancellationRequested();
            if (!_seen.Contains(path) && !_packageFiles.Contains(path) && !IsExcluded(path))
            {
                _findings.Missing.Add(path);
            }
        }

        return _findings;
    }

    private bool ScanDirectory(DirectoryInfo directory, string relative)
    {
        List<FileSystemInfo> entries;
        try
        {
            entries = [.. directory.EnumerateFileSystemInfos()];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Logger.LogWarning($"Could not list {directory.FullName} to clean it, it is left as it is:\n{e}");
            _skippedDirectories.Add(relative);
            return false;
        }

        bool empty = true;
        foreach (var entry in entries)
        {
            _ct.ThrowIfCancellationRequested();
            string path = relative.Length == 0 ? entry.Name : $"{relative}/{entry.Name}";
            bool removed = entry is DirectoryInfo child ? VisitDirectory(child, path) : VisitFile((FileInfo)entry, path);
            empty &= removed;
        }

        return empty;
    }

    private bool VisitDirectory(DirectoryInfo directory, string path)
    {
        if (IsLink(directory) || _protectedDirectories.Contains(PackagePaths.NormalizeDirectory(directory.FullName)) || _keep.IsKept(path))
        {
            _skippedDirectories.Add(path);
            return false;
        }

        if (!ScanDirectory(directory, path) || _expectedDirectories.Contains(path))
        {
            return false;
        }

        _findings.EmptyDirectories.Add(path);
        return true;
    }

    private bool VisitFile(FileInfo file, string path)
    {
        if (IsLink(file) || _keep.IsKept(path))
        {
            return false;
        }

        if (_packageFiles.Contains(path))
        {
            _seen.Add(path);
            return false;
        }

        if (_expected.TryGetValue(path, out var expected))
        {
            _seen.Add(path);
            Compare(file, path, expected);
            return false;
        }

        if (_packageDeletes.Contains(path))
        {
            return true;
        }

        if (ConfigFiles.IsConfigFile(path))
        {
            _findings.ProtectedConfigFiles.Add(path);
            return false;
        }

        _findings.Stray.Add(path);
        return true;
    }

    private void Compare(FileInfo file, string path, InventoryFile expected)
    {
        try
        {
            if (file.Length != expected.Size
                || !string.Equals(FileHashing.Sha256(file.FullName), expected.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                _findings.Mismatched.Add(path);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Logger.LogWarning($"Could not read {file.FullName} to compare it with the publish:\n{e}");
            _findings.Unreadable.Add(path);
        }
    }

    private bool IsExcluded(string path) =>
        _keep.IsKept(path)
        || _skippedDirectories.Exists(d => d.Length == 0 || path.StartsWith(d + "/", StringComparison.OrdinalIgnoreCase));

    private static bool IsLink(FileSystemInfo entry) => (entry.Attributes & FileAttributes.ReparsePoint) != 0;
}
