using System.Globalization;
using PublishTool.Helpers;

namespace PublishTool;

public sealed class DeployPreparer(string deployRoot, string publishRoot, IReadOnlyList<string> projectNames)
{
    public const string ChangesDirectoryName = "Deploy";
    private const string FallbackPrefix = "DEPLOY";
    private const string TimestampFormat = "yyyy-MM-dd_HH-mm-ss";
    private const int TimestampLength = 19;
    private const string UploadSuffix = ".uploading";
    private const int ReportEvery = 25;

    public string DeployRoot { get; } = deployRoot;

    public static async Task<string> ResolvePrefixAsync(string workingDirectory, CancellationToken ct)
    {
        try
        {
            var result = await ProcessHelper.RunAsync("git", "rev-parse --abbrev-ref HEAD", workingDirectory, cancellationToken: ct);
            string branch = result.Output.Trim();
            if (result.ExitCode != 0 || branch.Length == 0 || branch == "HEAD")
            {
                return FallbackPrefix;
            }

            return Sanitize(branch);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            Logger.LogWarning(e.ToString());
            return FallbackPrefix;
        }
    }

    public static string DirectoryName(string prefix, DateTimeOffset time) =>
        $"{prefix}_{time.ToString(TimestampFormat, CultureInfo.InvariantCulture)}";

    public string TargetPath(string prefix, DateTimeOffset time) => Path.Combine(DeployRoot, DirectoryName(prefix, time));

    public static string CreateStagingDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), "PublishTool", $"deploy-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    public int CopyPublishedProjects(string stage, Action<string> report, CancellationToken ct)
    {
        int total = 0;
        foreach (string project in projectNames)
        {
            string source = Path.Combine(publishRoot, project);
            if (!Directory.Exists(source))
            {
                throw new DirectoryNotFoundException($"Publish output of {project} was not found at {source}");
            }

            Directory.CreateDirectory(Path.Combine(stage, project));
            int count = 0;
            foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            {
                ct.ThrowIfCancellationRequested();
                CopyFile(file, Path.Combine(stage, project, Path.GetRelativePath(source, file)));
                count++;
            }

            report($"{project}: {count} files");
            total += count;
        }

        return total;
    }

    public string? FindPreviousDeploy(string prefix)
    {
        string? best = null;
        DateTime bestTime = DateTime.MinValue;
        foreach (string directory in Directory.EnumerateDirectories(DeployRoot))
        {
            if (TryParseName(Path.GetFileName(directory), out string candidatePrefix, out DateTime time)
                && string.Equals(candidatePrefix, prefix, StringComparison.OrdinalIgnoreCase)
                && time > bestTime)
            {
                best = directory;
                bestTime = time;
            }
        }

        return best;
    }

    public int CollectChanges(string stage, string? previous, Action<string> report, CancellationToken ct)
    {
        string changesRoot = Path.Combine(stage, ChangesDirectoryName);
        int changed = 0;
        foreach (string project in projectNames)
        {
            string current = Path.Combine(stage, project);
            foreach (string file in Directory.EnumerateFiles(current, "*", SearchOption.AllDirectories))
            {
                ct.ThrowIfCancellationRequested();
                string relative = Path.GetRelativePath(current, file);
                string? previousFile = previous is null ? null : Path.Combine(previous, project, relative);
                if (previousFile is not null && File.Exists(previousFile) && FilesAreEqual(file, previousFile))
                {
                    continue;
                }

                CopyFile(file, Path.Combine(changesRoot, project, relative));
                changed++;
                report(Path.Combine(project, relative));
            }
        }

        return changed;
    }

    public string Upload(string stage, string target, Action<string> report, CancellationToken ct)
    {
        string uploading = target + UploadSuffix;
        if (Directory.Exists(uploading))
        {
            Directory.Delete(uploading, recursive: true);
        }

        Directory.CreateDirectory(uploading);
        foreach (string directory in Directory.EnumerateDirectories(stage))
        {
            string name = Path.GetFileName(directory);
            Directory.CreateDirectory(Path.Combine(uploading, name));
            string[] files = Directory.GetFiles(directory, "*", SearchOption.AllDirectories);
            for (int i = 0; i < files.Length; i++)
            {
                ct.ThrowIfCancellationRequested();
                CopyFile(files[i], Path.Combine(uploading, name, Path.GetRelativePath(directory, files[i])));
                if ((i + 1) % ReportEvery == 0 || i == files.Length - 1)
                {
                    report($"{name}: {i + 1}/{files.Length} files");
                }
            }
        }

        return uploading;
    }

    public IReadOnlyList<string> CarryForwardMissingProjects(string target, string previous, Action<string> report, CancellationToken ct)
    {
        var carried = new List<string>();
        foreach (string directory in Directory.EnumerateDirectories(previous))
        {
            string project = Path.GetFileName(directory);
            if (string.Equals(project, ChangesDirectoryName, StringComparison.OrdinalIgnoreCase)
                || projectNames.Contains(project, StringComparer.OrdinalIgnoreCase)
                || Directory.Exists(Path.Combine(target, project)))
            {
                continue;
            }

            int count = 0;
            foreach (string file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
            {
                ct.ThrowIfCancellationRequested();
                CopyFile(file, Path.Combine(target, project, Path.GetRelativePath(directory, file)));
                count++;
            }

            report($"{project}: {count} files carried over from {Path.GetFileName(previous)}");
            carried.Add(project);
        }

        return carried;
    }

    public static void Commit(string uploading, string target) => Directory.Move(uploading, target);

    public static void TryDelete(string? directory)
    {
        if (directory is null || !Directory.Exists(directory))
        {
            return;
        }

        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (Exception e)
        {
            Logger.LogWarning($"Could not delete {directory}:\n{e}");
        }
    }

    private static void CopyFile(string source, string destination)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.Copy(source, destination, overwrite: true);
    }

    private static bool FilesAreEqual(string first, string second)
    {
        var firstInfo = new FileInfo(first);
        var secondInfo = new FileInfo(second);
        if (firstInfo.Length != secondInfo.Length)
        {
            return false;
        }

        const int bufferSize = 1024 * 1024;
        using var firstStream = new FileStream(first, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize, FileOptions.SequentialScan);
        using var secondStream = new FileStream(second, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize, FileOptions.SequentialScan);
        byte[] firstBuffer = new byte[bufferSize];
        byte[] secondBuffer = new byte[bufferSize];
        while (true)
        {
            int firstRead = firstStream.ReadAtLeast(firstBuffer, bufferSize, throwOnEndOfStream: false);
            int secondRead = secondStream.ReadAtLeast(secondBuffer, bufferSize, throwOnEndOfStream: false);
            if (firstRead != secondRead || !firstBuffer.AsSpan(0, firstRead).SequenceEqual(secondBuffer.AsSpan(0, secondRead)))
            {
                return false;
            }

            if (firstRead == 0)
            {
                return true;
            }
        }
    }

    private static bool TryParseName(string name, out string prefix, out DateTime time)
    {
        prefix = string.Empty;
        time = default;
        int separator = name.Length - TimestampLength - 1;
        if (separator < 1 || name[separator] != '_')
        {
            return false;
        }

        if (!DateTime.TryParseExact(name[(separator + 1)..], TimestampFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out time))
        {
            return false;
        }

        prefix = name[..separator];
        return true;
    }

    private static string Sanitize(string branch)
    {
        char[] invalid = [.. Path.GetInvalidFileNameChars(), '/', '\\'];
        return string.Concat(branch.Select(c => invalid.Contains(c) ? '-' : c));
    }
}
