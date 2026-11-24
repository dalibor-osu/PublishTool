using System.Formats.Tar;
using System.IO.Compression;
using PublishTool.Helpers;

namespace PublishTool.GitHub;

public sealed class SelfUpdate
{
    private const string BackupSuffix = ".old";

    private readonly Func<string, string, string, string> _assetName;

    private SelfUpdate(string appName, Func<string, string, string, string> assetName)
    {
        AppName = appName;
        _assetName = assetName;
    }

    public static SelfUpdate Tui { get; } = new("PublishTool", (tag, rid, extension) => $"PublishTool-{tag}-{rid}.{extension}");

    public static SelfUpdate Cli { get; } = new("PublishToolCli", (tag, rid, extension) => $"PublishToolCli-{rid}-{tag}.{extension}");

    public string AppName { get; }

    public string ExecutableName => AppName + (OperatingSystem.IsWindows() ? ".exe" : string.Empty);

    public string AssetName(ReleaseVersion release) => _assetName(
        release.TagName,
        $"{EnvironmentHelper.GetCurrentOsShortcut()}-{EnvironmentHelper.GetCurrentArchitecture()}",
        EnvironmentHelper.GetPlatformArchiveExtension());

    public static void CleanUpBackup()
    {
        if (Environment.ProcessPath == null)
        {
            return;
        }

        string backupPath = Environment.ProcessPath + BackupSuffix;

        try
        {
            if (File.Exists(backupPath))
            {
                File.Delete(backupPath);
                Logger.LogInfo($"Deleted update backup '{backupPath}'");
            }
        }
        catch (Exception e)
        {
            Logger.LogWarning($"Failed to delete update backup '{backupPath}': {e}");
        }
    }

    public static Task<Result<ReleaseVersion>> FindAsync(GitHubClient client, bool preRelease, string? version, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(version))
        {
            return client.GetVersionAsync(version, ct);
        }

        return preRelease ? client.GetLatestVersionIncludingPreReleaseAsync(ct) : client.GetLatestFullVersionAsync(ct);
    }

    public async Task<Result<bool>> InstallAsync(GitHubClient client, ReleaseVersion release, Action<string> report, CancellationToken ct)
    {
        string assetName = AssetName(release);
        string downloadUrl = release.FindAssetUrl(assetName);
        if (downloadUrl.Length == 0)
        {
            return $"The release {release} has no asset {assetName}";
        }

        string currentExePath = Environment.ProcessPath ?? throw new InvalidOperationException("Cannot get executable path");
        string tempDirectory = Path.Combine(Path.GetTempPath(), $"{AppName}-update-{Guid.NewGuid():N}");

        try
        {
            report($"Downloading {assetName}...");
            var archive = await client.DownloadAssetAsync(downloadUrl, tempDirectory, ct);
            if (!archive.IsSuccess)
            {
                return "Failed to download the new version";
            }

            report("Extracting the new version...");
            string extractDirectory = Path.Combine(tempDirectory, "extracted");
            var extractResult = ExtractArchive(archive.Value, extractDirectory);
            if (!extractResult.IsSuccess)
            {
                return $"Failed to extract the new version: {extractResult.Error}";
            }

            string? executable = Directory.EnumerateFiles(extractDirectory, ExecutableName, SearchOption.AllDirectories).FirstOrDefault();
            if (executable is null)
            {
                return $"Could not find '{ExecutableName}' in the downloaded archive";
            }

            var replaceResult = ReplaceExecutable(executable, currentExePath);
            if (!replaceResult.IsSuccess)
            {
                return $"Failed to replace the current executable: {replaceResult.Error}";
            }

            return true;
        }
        finally
        {
            TryDeleteDirectory(tempDirectory);
        }
    }

    private static Result<bool> ExtractArchive(FileInfo archive, string targetDirectory)
    {
        try
        {
            Directory.CreateDirectory(targetDirectory);

            if (archive.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                ZipFile.ExtractToDirectory(archive.FullName, targetDirectory, true);
                return true;
            }

            if (archive.Name.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase))
            {
                using var archiveStream = archive.OpenRead();
                using var decompressed = new GZipStream(archiveStream, CompressionMode.Decompress);
                TarFile.ExtractToDirectory(decompressed, targetDirectory, true);
                return true;
            }

            return $"Unsupported archive format '{archive.Name}'";
        }
        catch (Exception e)
        {
            Logger.LogError(e.ToString());
            return "An error occurred while extracting the archive";
        }
    }

    private static Result<bool> ReplaceExecutable(string newExecutablePath, string currentExePath)
    {
        string backupPath = currentExePath + BackupSuffix;

        try
        {
            if (File.Exists(backupPath))
            {
                File.Delete(backupPath);
            }

            File.Move(currentExePath, backupPath);
        }
        catch (Exception e)
        {
            Logger.LogError(e.ToString());
            return $"Could not rename the current executable to '{Path.GetFileName(backupPath)}'";
        }

        try
        {
            File.Move(newExecutablePath, currentExePath);
        }
        catch (Exception e)
        {
            Logger.LogError(e.ToString());

            try
            {
                File.Move(backupPath, currentExePath);
            }
            catch (Exception rollbackException)
            {
                Logger.LogError(rollbackException.ToString());
                return $"Could not install the new executable and the previous one is left at '{backupPath}'";
            }

            return "Could not install the new executable";
        }

        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(currentExePath,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        }

        return true;
    }

    private static void TryDeleteDirectory(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
        catch (Exception e)
        {
            Logger.LogWarning($"Failed to delete '{directory}': {e}");
        }
    }
}
