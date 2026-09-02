using System.Formats.Tar;
using System.IO.Compression;
using PublishTool.Extensions;
using PublishTool.GitHub;
using PublishTool.Helpers;
using Spectre.Console;

namespace PublishTool.Commands;

public class UpdateCommand(UpdateCommand.Options options) : Command<UpdateCommand.Options>(options)
{
    private const string ExecutableName = "PublishTool";
    private const string BackupSuffix = ".old";

    public new class Options
    {
        public string? Version { get; set; }
        public bool PreRelease { get; set; } = false;
        public bool Fetch { get; set; } = false;
    }

    public override bool UsesAlternateScreen => false;

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


    public override async Task<int> ExecuteAsync(CancellationToken ct)
    {
        var client = new GitHubClient();
        var currentVersion = ReleaseVersion.Current;
        string preRelease = base.Options.PreRelease ? "pre-release" : "full";

        if (currentVersion?.IsPreRelease == true)
        {
            preRelease = await AnsiConsole.ConfirmAsync(
                "You are currently running a pre-release version. Do you want to fetch the latest pre-release version?", cancellationToken: ct)
                ? "pre-release"
                : "full";
        }

        AnsiConsole.WriteLine($"Fetching latest {preRelease} version...");
        var result = base.Options.PreRelease ? await client.GetLatestVersionIncludingPreReleaseAsync(ct) : await client.GetLatestFullVersionAsync(ct);
        if (!result.IsSuccess)
        {
            AnsiConsole.MarkupLine($"[red]Failed to fetch latest {preRelease} version:[/] {result.Error}");
            return 1;
        }

        var latestVersion = result.Value;

        if (base.Options.Fetch)
        {
            AnsiConsole.WriteLine($"Latest {preRelease} version: {latestVersion} (Current: {BuildInfo.Version})");
            return 0;
        }

        if (EnvironmentHelper.IsDevVersion || currentVersion == null)
        {
            AnsiConsole.WriteLine("Cannot update dev version");
            return 1;
        }

        int compareResult = currentVersion.CompareTo(latestVersion);
        if (compareResult == 0)
        {
            AnsiConsole.WriteLine("Current version is up to date");
            return 0;
        }

        bool update = true;
        if (compareResult > 0)
        {
            update = await AnsiConsole.ConfirmAsync(
                $"You are running a version newer than the latest version. Do you want to downgrade to the latest published {preRelease} version ({latestVersion})?",
                cancellationToken: ct);
        }

        if (!update)
        {
            AnsiConsole.WriteLine("Update cancelled");
            return 0;
        }

        string downloadUrl = latestVersion.GetDownloadUrl();
        if (downloadUrl.IsEmptyOrWhiteSpace)
        {
            AnsiConsole.WriteLine("Cannot get download url");
            return 1;
        }

        string currentExePath = Environment.ProcessPath ?? throw new InvalidOperationException("Cannot get executable path");
        string tempDirectory = Path.Combine(Path.GetTempPath(), $"PublishTool-update-{Guid.NewGuid():N}");

        try
        {
            var assetArchiveInfo = await client.DownloadAssetAsync(downloadUrl, tempDirectory, ct);
            if (!assetArchiveInfo.IsSuccess)
            {
                AnsiConsole.WriteLine("Failed to download the new version");
                return 1;
            }

            AnsiConsole.WriteLine("Extracting the new version...");
            string extractDirectory = Path.Combine(tempDirectory, "extracted");
            var extractResult = ExtractArchive(assetArchiveInfo.Value, extractDirectory);
            if (!extractResult.IsSuccess)
            {
                AnsiConsole.MarkupLine($"[red]Failed to extract the new version:[/] {extractResult.Error}");
                return 1;
            }

            var newExecutableResult = FindExecutable(extractDirectory);
            if (!newExecutableResult.IsSuccess)
            {
                AnsiConsole.MarkupLine($"[red]Failed to extract the new version:[/] {newExecutableResult.Error}");
                return 1;
            }

            var replaceResult = ReplaceExecutable(newExecutableResult.Value.FullName, currentExePath);
            if (!replaceResult.IsSuccess)
            {
                AnsiConsole.MarkupLine($"[red]Failed to replace the current executable:[/] {replaceResult.Error}");
                return 1;
            }

            AnsiConsole.MarkupLine($"[green]Updated to {latestVersion}[/]");
            return 0;
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
                ZipFile.ExtractToDirectory(archive.FullName, targetDirectory, overwriteFiles: true);
                return true;
            }

            if (archive.Name.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase))
            {
                using var archiveStream = archive.OpenRead();
                using var decompressed = new GZipStream(archiveStream, CompressionMode.Decompress);
                TarFile.ExtractToDirectory(decompressed, targetDirectory, overwriteFiles: true);
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

    private static Result<FileInfo> FindExecutable(string extractDirectory)
    {
        string executableName = ExecutableName + (OperatingSystem.IsWindows() ? ".exe" : string.Empty);

        string? executable = Directory
            .EnumerateFiles(extractDirectory, executableName, SearchOption.AllDirectories)
            .FirstOrDefault();

        if (executable != null)
        {
            return new FileInfo(executable);
        }

        var candidates = Directory
            .EnumerateFiles(extractDirectory, "*", SearchOption.AllDirectories)
            .Where(f => !Path.GetFileNameWithoutExtension(f).Equals("LICENSE", StringComparison.OrdinalIgnoreCase))
            .Take(2)
            .ToList();

        if (candidates.Count != 1)
        {
            return $"Could not find '{executableName}' in the downloaded archive";
        }

        return new FileInfo(candidates[0]);
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
                Directory.Delete(directory, recursive: true);
            }
        }
        catch (Exception e)
        {
            Logger.LogWarning($"Failed to delete '{directory}': {e}");
        }
    }
}