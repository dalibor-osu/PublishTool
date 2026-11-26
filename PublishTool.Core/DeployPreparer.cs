using System.Globalization;
using System.IO.Compression;
using PublishTool.Configuration;
using PublishTool.Deployment;
using PublishTool.Helpers;

namespace PublishTool;

public sealed class DeployPreparer(string sharedRoot, string deployRoot, string publishRoot, IReadOnlyList<string> projectNames)
{
    private const string FallbackPrefix = "DEPLOY";
    private const string TimestampFormat = "yyyy-MM-dd_HH-mm-ss";
    private const int TimestampLength = 19;
    private const string ArchiveExtension = ".zip";
    private const string UploadSuffix = ".uploading";
    private const string WorkSuffix = ".tmp";
    private const string PreviousArchiveName = "previous.zip";
    private const string PreviousDirectoryName = "previous";
    private const int ReportEvery = 25;
    private const int CopyBufferSize = 4 * 1024 * 1024;
    private const CompressionLevel ArchiveCompression = CompressionLevel.Fastest;
    private const string LegacyChangesDirectoryName = "Deploy";

    public string SharedRoot { get; } = sharedRoot;
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

    public string DeployPath(string name) => Path.Combine(DeployRoot, name);

    public string WorkPath(string name) => Path.Combine(DeployRoot, name + WorkSuffix);

    public string ArchivePath(string name) => Path.Combine(WorkPath(name), name + ArchiveExtension);

    public string ReferencePath(string name) => Path.Combine(SharedRoot, name + ArchiveExtension);

    public static string UploadPath(string reference) => reference + UploadSuffix;

    public string? FindPreviousReference(string prefix)
    {
        string? bestArchive = null;
        DateTime bestArchiveTime = DateTime.MinValue;
        string? legacy = null;
        DateTime legacyTime = DateTime.MinValue;
        foreach (var (path, isDirectory, time) in EnumerateReferences(prefix))
        {
            if (isDirectory)
            {
                if (time > legacyTime)
                {
                    legacy = path;
                    legacyTime = time;
                }
            }
            else if (time > bestArchiveTime)
            {
                bestArchive = path;
                bestArchiveTime = time;
            }
        }

        if (legacy is not null && legacyTime > bestArchiveTime)
        {
            throw new InvalidOperationException(
                $"{legacy} is a reference from the old workflow that stored the publish as a directory. Compress its contents into "
                + $"{legacy}{ArchiveExtension} (project directories at the root of the archive) and delete the directory, or delete "
                + "the directory if the reference is no longer needed.");
        }

        return bestArchive;
    }

    public IReadOnlyList<string> PruneReferences(string prefix, int keep, Action<string> report, CancellationToken ct)
    {
        var deleted = new List<string>();
        var stale = EnumerateReferences(prefix)
            .Where(r => !r.IsDirectory)
            .OrderByDescending(r => r.Time)
            .Skip(keep);
        foreach (var (path, _, _) in stale)
        {
            ct.ThrowIfCancellationRequested();
            string name = Path.GetFileName(path);
            try
            {
                File.Delete(path);
                deleted.Add(name);
                report($"deleted {name}");
            }
            catch (Exception e)
            {
                Logger.LogWarning($"Could not delete old reference {path}:\n{e}");
                report($"could not delete {name}: {e.Message}");
            }
        }

        return deleted;
    }

    private IEnumerable<(string Path, bool IsDirectory, DateTime Time)> EnumerateReferences(string prefix)
    {
        foreach (var entry in new DirectoryInfo(SharedRoot).EnumerateFileSystemInfos())
        {
            bool isDirectory = entry is DirectoryInfo;
            if (!isDirectory && !string.Equals(entry.Extension, ArchiveExtension, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string name = isDirectory ? entry.Name : Path.GetFileNameWithoutExtension(entry.Name);
            if (TryParseName(name, out string candidatePrefix, out DateTime time)
                && string.Equals(candidatePrefix, prefix, StringComparison.OrdinalIgnoreCase))
            {
                yield return (entry.FullName, isDirectory, time);
            }
        }
    }

    public static (string Directory, int Files) Download(string reference, string workDirectory, Action<string> report, CancellationToken ct)
    {
        Directory.CreateDirectory(workDirectory);
        string archive = Path.Combine(workDirectory, PreviousArchiveName);
        string extracted = Path.Combine(workDirectory, PreviousDirectoryName);
        CopyWithProgress(reference, archive, report, ct);
        int files = Extract(archive, extracted, report, ct);
        File.Delete(archive);
        return (extracted, files);
    }

    public ChangeSet CollectChanges(string deployDirectory, string? previous, string name, Action<string> report, CancellationToken ct)
    {
        Directory.CreateDirectory(deployDirectory);
        var previousLineage = previous is null ? null : PackageJson.TryReadLineage(Path.Combine(previous, ReferenceLineage.FileName));
        var previousInventory = previous is null ? null : PackageJson.TryReadInventory(Path.Combine(previous, PackageInventory.FileName));
        var lineage = new ReferenceLineage();
        var inventory = new PackageInventory { Package = name };
        var projects = new List<ManifestProject>();
        foreach (string project in projectNames)
        {
            var listed = new InventoryProject { Name = project };
            inventory.Projects.Add(listed);
            var collected = CollectProject(project, deployDirectory, previous, listed, previousInventory?.Find(project), report, ct);
            string? previousPublisher = FindPublisher(previousLineage, project);
            if (collected.Files.Count == 0 && collected.Deleted.Count == 0)
            {
                if (previousPublisher is not null)
                {
                    lineage.Projects[project] = previousPublisher;
                }

                continue;
            }

            collected.Base = collected.IsFullPublish ? null : previousPublisher;
            lineage.Projects[project] = name;
            projects.Add(collected);
        }

        if (previousLineage is not null)
        {
            foreach (var (project, publisher) in previousLineage.Projects)
            {
                if (!projectNames.Contains(project, StringComparer.OrdinalIgnoreCase))
                {
                    lineage.Projects[project] = publisher;
                }
            }
        }

        foreach (string directory in CarriedDirectories(previous))
        {
            inventory.Projects.Add(CarriedInventory(directory, previousInventory, ct));
        }

        return new ChangeSet(projects, lineage, inventory);
    }

    public static void WriteInventory(string deployDirectory, PackageInventory inventory) =>
        PackageJson.WriteInventory(Path.Combine(deployDirectory, PackageInventory.FileName), inventory);

    public static string PrepareManifest(string workDirectory, PackageManifest manifest)
    {
        Directory.CreateDirectory(workDirectory);
        string path = Path.Combine(workDirectory, PackageManifest.FileName);
        PackageJson.WriteManifest(path, manifest);
        return path;
    }

    public static void PlaceManifest(string preparedManifest, string deployDirectory) =>
        File.Move(preparedManifest, Path.Combine(deployDirectory, PackageManifest.FileName), overwrite: true);

    public ArchiveInfo CreateArchive(
        string archive, string? previous, ReferenceLineage lineage, PackageInventory inventory, Action<string> report, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(archive)!);
        File.Delete(archive);
        int total = 0;
        var carried = new List<string>();
        using (var zip = ZipFile.Open(archive, ZipArchiveMode.Create))
        {
            foreach (string project in projectNames)
            {
                total += AddProject(zip, project, PublishedProjectDirectory(project), report, ct);
            }

            foreach (string directory in CarriedDirectories(previous))
            {
                string project = Path.GetFileName(directory);
                int count = AddProject(zip, project, directory, _ => { }, ct);
                report($"{project}: {count} files carried over from the previous publish");
                total += count;
                carried.Add(project);
            }

            using (var lineageStream = zip.CreateEntry(ReferenceLineage.FileName, ArchiveCompression).Open())
            {
                PackageJson.WriteLineage(lineageStream, lineage);
            }

            using var inventoryStream = zip.CreateEntry(PackageInventory.FileName, ArchiveCompression).Open();
            PackageJson.WriteInventory(inventoryStream, inventory);
        }

        return new ArchiveInfo(archive, total, new FileInfo(archive).Length, carried);
    }

    public static string Upload(string archive, string reference, Action<string> report, CancellationToken ct)
    {
        string uploading = UploadPath(reference);
        Directory.CreateDirectory(Path.GetDirectoryName(reference)!);
        CopyWithProgress(archive, uploading, report, ct);
        return uploading;
    }

    public static void Commit(string uploading, string reference) => File.Move(uploading, reference);

    public static void TryDelete(string? path)
    {
        if (path is null)
        {
            return;
        }

        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
            else if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception e)
        {
            Logger.LogWarning($"Could not delete {path}:\n{e}");
        }
    }

    public static string FormatSize(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return unit == 0
            ? $"{bytes} {units[unit]}"
            : $"{value.ToString("0.#", CultureInfo.InvariantCulture)} {units[unit]}";
    }

    public readonly record struct ArchiveInfo(string Path, int Files, long Size, IReadOnlyList<string> Carried);

    public sealed record ChangeSet(IReadOnlyList<ManifestProject> Projects, ReferenceLineage Lineage, PackageInventory Inventory)
    {
        public int Changed => Projects.Sum(p => p.Files.Count);

        public int Deleted => Projects.Sum(p => p.Deleted.Count);
    }

    private IEnumerable<string> CarriedDirectories(string? previous) =>
        previous is null
            ? []
            : Directory.EnumerateDirectories(previous).Where(directory =>
            {
                string project = Path.GetFileName(directory);
                return !string.Equals(project, LegacyChangesDirectoryName, StringComparison.OrdinalIgnoreCase)
                    && !projectNames.Contains(project, StringComparer.OrdinalIgnoreCase);
            });

    private static InventoryProject CarriedInventory(string directory, PackageInventory? previousInventory, CancellationToken ct)
    {
        string project = Path.GetFileName(directory);
        if (previousInventory?.Find(project) is { } known)
        {
            return new InventoryProject { Name = project, Files = [.. known.Files] };
        }

        var listed = new InventoryProject { Name = project };
        foreach (string file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
        {
            ct.ThrowIfCancellationRequested();
            listed.Files.Add(new InventoryFile
            {
                Path = PackagePaths.ToManifest(Path.GetRelativePath(directory, file)),
                Size = new FileInfo(file).Length,
                Sha256 = FileHashing.Sha256(file)
            });
        }

        return listed;
    }

    private static Dictionary<string, string> KnownHashes(InventoryProject? previousInventory)
    {
        var hashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in previousInventory?.Files ?? [])
        {
            hashes[file.Path] = file.Sha256;
        }

        return hashes;
    }

    private ManifestProject CollectProject(
        string project,
        string deployDirectory,
        string? previous,
        InventoryProject listed,
        InventoryProject? previousInventory,
        Action<string> report,
        CancellationToken ct)
    {
        var knownHashes = KnownHashes(previousInventory);
        string source = PublishedProjectDirectory(project);
        string? previousProject = previous is not null && Directory.Exists(Path.Combine(previous, project))
            ? Path.Combine(previous, project)
            : null;
        var collected = new ManifestProject { Name = project, IsFullPublish = previousProject is null };
        var current = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            ct.ThrowIfCancellationRequested();
            string relative = Path.GetRelativePath(source, file);
            string manifestPath = PackagePaths.ToManifest(relative);
            current.Add(relative);
            string? previousFile = PreviousFile(previousProject, relative);
            if (previousFile is not null && FilesAreEqual(file, previousFile))
            {
                listed.Files.Add(new InventoryFile
                {
                    Path = manifestPath,
                    Size = new FileInfo(file).Length,
                    Sha256 = knownHashes.GetValueOrDefault(manifestPath) ?? FileHashing.Sha256(file)
                });
                continue;
            }

            string destination = Path.Combine(deployDirectory, project, relative);
            CopyFile(file, destination);
            var changed = new ManifestFile
            {
                Path = manifestPath,
                Size = new FileInfo(destination).Length,
                Sha256 = FileHashing.Sha256(destination),
                PreviousSha256 = previousFile is null ? null : FileHashing.Sha256(previousFile)
            };
            collected.Files.Add(changed);
            listed.Files.Add(new InventoryFile { Path = changed.Path, Size = changed.Size, Sha256 = changed.Sha256 });
            report(Path.Combine(project, relative));
        }

        if (previousProject is not null)
        {
            foreach (string file in Directory.EnumerateFiles(previousProject, "*", SearchOption.AllDirectories))
            {
                ct.ThrowIfCancellationRequested();
                string relative = Path.GetRelativePath(previousProject, file);
                if (!current.Contains(relative))
                {
                    collected.Deleted.Add(new ManifestDeletion { Path = PackagePaths.ToManifest(relative), Sha256 = FileHashing.Sha256(file) });
                    report($"{Path.Combine(project, relative)} (deleted)");
                }
            }
        }

        return collected;
    }

    private static string? PreviousFile(string? previousProject, string relative)
    {
        if (previousProject is null)
        {
            return null;
        }

        string path = Path.Combine(previousProject, relative);
        return File.Exists(path) ? path : null;
    }

    private static string? FindPublisher(ReferenceLineage? lineage, string project)
    {
        if (lineage is null)
        {
            return null;
        }

        string? key = ServerConfig.FindKey(lineage.Projects, project);
        return key is null ? null : lineage.Projects[key];
    }

    private static int AddProject(ZipArchive zip, string project, string source, Action<string> report, CancellationToken ct)
    {
        string[] files = Directory.GetFiles(source, "*", SearchOption.AllDirectories);
        for (int i = 0; i < files.Length; i++)
        {
            ct.ThrowIfCancellationRequested();
            string entryName = Path.Combine(project, Path.GetRelativePath(source, files[i])).Replace('\\', '/');
            zip.CreateEntryFromFile(files[i], entryName, ArchiveCompression);
            if ((i + 1) % ReportEvery == 0 || i == files.Length - 1)
            {
                report($"{project}: {i + 1}/{files.Length} files");
            }
        }

        return files.Length;
    }

    private static int Extract(string archive, string target, Action<string> report, CancellationToken ct)
    {
        target = Path.GetFullPath(target);
        string targetPrefix = Path.EndsInDirectorySeparator(target) ? target : target + Path.DirectorySeparatorChar;
        Directory.CreateDirectory(target);
        int extracted = 0;
        using var zip = ZipFile.OpenRead(archive);
        int count = zip.Entries.Count;
        foreach (var entry in zip.Entries)
        {
            ct.ThrowIfCancellationRequested();
            if (entry.FullName.EndsWith('/'))
            {
                continue;
            }

            string destination = Path.GetFullPath(Path.Combine(target, entry.FullName));
            if (!destination.StartsWith(targetPrefix, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"Archive entry {entry.FullName} points outside of {target}");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            entry.ExtractToFile(destination, overwrite: true);
            extracted++;
            if (extracted % ReportEvery == 0 || extracted == count)
            {
                report($"{extracted}/{count} files extracted");
            }
        }

        return extracted;
    }

    private static void CopyWithProgress(string source, string destination, Action<string> report, CancellationToken ct)
    {
        long total = new FileInfo(source).Length;
        using var reader = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, CopyBufferSize, FileOptions.SequentialScan);
        using var writer = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None, CopyBufferSize, FileOptions.SequentialScan);
        byte[] buffer = new byte[CopyBufferSize];
        long copied = 0;
        int lastPercent = -1;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            int read = reader.Read(buffer, 0, buffer.Length);
            if (read == 0)
            {
                break;
            }

            writer.Write(buffer, 0, read);
            copied += read;
            int percent = total == 0 ? 100 : (int)(copied * 100 / total);
            if (percent != lastPercent)
            {
                lastPercent = percent;
                report($"{FormatSize(copied)}/{FormatSize(total)} ({percent}%)");
            }
        }
    }

    private string PublishedProjectDirectory(string project)
    {
        string source = Path.Combine(publishRoot, project);
        if (!Directory.Exists(source))
        {
            throw new DirectoryNotFoundException($"Publish output of {project} was not found at {source}");
        }

        return source;
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
