using PublishTool.Deployment;
using PublishTool.Helpers;
using PublishTool.Progress;

namespace PublishTool;

public sealed record PublishSettings(string PublishDirectory, string? SharedDirectory, string? DeployDirectory);

public sealed record PublishedProject(string Name, int Changed, int Deleted, bool IsFullPublish, string? Base);

public sealed class PublishResult
{
    public bool Failed { get; set; }

    public bool Cancelled { get; set; }

    public bool Succeeded => !Failed && !Cancelled;

    public string? Prefix { get; set; }

    public string? PackageName { get; set; }

    public string? PackageDirectory { get; set; }

    public string? Reference { get; set; }

    public string? PreviousReference { get; set; }

    public List<PublishedProject> Projects { get; } = [];

    public List<string> Carried { get; } = [];

    public List<string> Pruned { get; } = [];
}

public static class BuildConfigurations
{
    public const string Release = "Release";
    public const string Debug = "Debug";

    public static readonly IReadOnlyList<string> All = [Release, Debug];

    public static string? Find(string value) => All.FirstOrDefault(c => string.Equals(c, value, StringComparison.OrdinalIgnoreCase));
}

public sealed class PublishWorkflow
{
    public const int KeepReferences = 3;

    private readonly string _workingDirectory;
    private readonly IReadOnlyList<DotnetProject> _projects;
    private readonly string _configuration;
    private readonly IStep _buildRow;
    private readonly List<(PublishCommandBuilder Command, IStep Row)> _publishRows;
    private readonly DeployStep? _deployStep;
    private readonly PublishResult _result = new();
    private volatile bool _hasError;

    public PublishWorkflow(
        string workingDirectory,
        IReadOnlyList<DotnetProject> projects,
        string configuration,
        PublishSettings settings,
        bool local,
        IStepList steps)
    {
        _workingDirectory = workingDirectory;
        _projects = projects;
        _configuration = configuration;
        _buildRow = steps.Add("Building projects and their dependencies");
        _publishRows = projects
            .Select(p => new PublishCommandBuilder(p, configuration, Path.Combine(settings.PublishDirectory, p.Name)))
            .Select(c => (Command: c, Row: steps.Add($"Publishing {c.Project.Name}")))
            .ToList();

        if (local)
        {
            return;
        }

        var preparer = new DeployPreparer(
            settings.SharedDirectory ?? throw new ArgumentException("The shared directory is required unless publishing locally", nameof(settings)),
            settings.DeployDirectory ?? throw new ArgumentException("The deploy directory is required unless publishing locally", nameof(settings)),
            settings.PublishDirectory,
            projects.Select(p => p.Name).ToList());
        _deployStep = new DeployStep(
            preparer,
            steps.Add("Downloading the previous publish of this branch from the shared directory"),
            steps.Add("Collecting changed files into the deploy directory"),
            steps.Add("Compressing the full publish into an archive"),
            steps.Add("Uploading the archive to the shared directory"),
            steps.Add($"Deleting old publishes of this branch from the shared directory (keeping the last {KeepReferences})"));
    }

    public static async Task<bool> IsMsBuildAvailableAsync()
    {
        try
        {
            var result = await ProcessHelper.RunAsync("MSBuild", "/version");
            return result.ExitCode == 0;
        }
        catch (Exception e)
        {
            Logger.LogError(e.ToString());
            return false;
        }
    }

    public Task<PublishResult> RunAsync(CancellationToken ct) => Task.Run(() => ExecuteAsync(ct), CancellationToken.None);

    private async Task<PublishResult> ExecuteAsync(CancellationToken ct)
    {
        await PublishAll(ct);
        _result.Failed = _hasError;
        _result.Cancelled = ct.IsCancellationRequested;
        return _result;
    }

    private sealed record DeployStep(
        DeployPreparer Preparer,
        IStep DownloadRow,
        IStep ChangesRow,
        IStep ArchiveRow,
        IStep UploadRow,
        IStep PruneRow)
    {
        public IEnumerable<IStep> Rows => [DownloadRow, ChangesRow, ArchiveRow, UploadRow, PruneRow];
    }

    private async Task PublishAll(CancellationToken ct)
    {
        string? prefix = await ResolvePrefix(ct);
        if (prefix is null)
        {
            SkipPublishes("no usable package prefix", ct);
            return;
        }

        bool built = await BuildDependencies(prefix, ct);
        if (!built)
        {
            SkipPublishes("build failed", ct);
            return;
        }

        await Task.WhenAll(_publishRows.Select(item => PublishProject(item.Command, prefix, item.Row, ct)));

        if (_deployStep is null)
        {
            return;
        }

        if (_hasError || ct.IsCancellationRequested)
        {
            SkipDeploy("publish failed", ct);
            return;
        }

        await PrepareDeploy(_deployStep, prefix, ct);
    }

    private async Task<string?> ResolvePrefix(CancellationToken ct)
    {
        string prefix;
        try
        {
            prefix = await DeployPreparer.ResolvePrefixAsync(_workingDirectory, ct);
        }
        catch (OperationCanceledException)
        {
            _buildRow.Finish(StepState.Cancelled);
            return null;
        }

        _result.Prefix = prefix;
        if (MsBuildProperties.PrefixProblem(prefix) is { } problem)
        {
            _hasError = true;
            Logger.LogError(problem);
            _buildRow.Finish(StepState.Failed, problem);
            return null;
        }

        _buildRow.Name = $"Building projects and their dependencies ({MsBuildProperties.Prefix}={prefix})";
        return prefix;
    }

    private void SkipPublishes(string reason, CancellationToken ct)
    {
        foreach (var (_, row) in _publishRows)
        {
            row.Finish(ct.IsCancellationRequested ? StepState.Cancelled : StepState.Skipped, reason);
        }

        SkipDeploy(reason, ct);
    }

    private void SkipDeploy(string reason, CancellationToken ct)
    {
        if (_deployStep is null)
        {
            return;
        }

        var state = ct.IsCancellationRequested ? StepState.Cancelled : StepState.Skipped;
        foreach (var row in _deployStep.Rows)
        {
            row.Finish(state, reason);
        }
    }

    private async Task PrepareDeploy(DeployStep step, string prefix, CancellationToken ct)
    {
        var (preparer, downloadRow, changesRow, archiveRow, uploadRow, pruneRow) = step;
        string? name = null;
        string? work = null;
        string? deployDirectory = null;
        string? uploading = null;
        try
        {
            name = DeployPreparer.DirectoryName(prefix, DateTimeOffset.Now);
            string? previousReference = preparer.FindPreviousReference(prefix);
            work = preparer.WorkPath(name);
            _result.PreviousReference = previousReference;

            string? previous = await DownloadAsync(downloadRow, previousReference, work, ct);

            string packageDirectory = preparer.DeployPath(name);
            deployDirectory = packageDirectory;
            changesRow.Name = previousReference is null
                ? $"Collecting changed files into {packageDirectory} (everything is new)"
                : $"Collecting changed files into {packageDirectory} (compared with {Path.GetFileName(previousReference)})";
            changesRow.Start();
            string packageName = name;
            var changes = await Task.Run(() => preparer.CollectChanges(packageDirectory, previous, packageName, changesRow.AppendLine, ct), ct);
            string preparedManifest = DeployPreparer.PrepareManifest(work, new PackageManifest
            {
                ToolVersion = BuildInfo.Version,
                Name = name,
                Prefix = prefix,
                Created = DateTimeOffset.Now,
                PreviousReference = previousReference is null ? null : Path.GetFileName(previousReference),
                Projects = [.. changes.Projects]
            });
            DeployPreparer.WriteInventory(packageDirectory, changes.Inventory);
            changesRow.Finish(StepState.Succeeded, $"{changes.Changed} files changed, {changes.Deleted} deleted");
            deployDirectory = null;
            _result.Projects.AddRange(changes.Projects.Select(p => new PublishedProject(p.Name, p.Files.Count, p.Deleted.Count, p.IsFullPublish, p.Base)));

            string archive = preparer.ArchivePath(name);
            archiveRow.Name = $"Compressing the full publish into {archive}";
            archiveRow.Start();
            var archiveInfo = await Task.Run(() => preparer.CreateArchive(archive, previous, changes.Lineage, changes.Inventory, archiveRow.AppendLine, ct), ct);
            _result.Carried.AddRange(archiveInfo.Carried);
            archiveRow.Finish(StepState.Succeeded, archiveInfo.Carried.Count == 0
                ? $"{archiveInfo.Files} files, {DeployPreparer.FormatSize(archiveInfo.Size)}"
                : $"{archiveInfo.Files} files, {DeployPreparer.FormatSize(archiveInfo.Size)}, {archiveInfo.Carried.Count} unpublished "
                  + $"project(s) carried over from {Path.GetFileName(previousReference)}");

            string newReference = preparer.ReferencePath(name);
            uploadRow.Name = $"Uploading {Path.GetFileName(newReference)} to {preparer.SharedRoot}";
            uploadRow.Start();
            uploading = DeployPreparer.UploadPath(newReference);
            await Task.Run(() => DeployPreparer.Upload(archive, newReference, uploadRow.AppendLine, ct), ct);
            DeployPreparer.Commit(uploading, newReference);
            uploading = null;
            PlaceManifest(preparedManifest, packageDirectory, newReference);
            uploadRow.Finish(StepState.Succeeded, DeployPreparer.FormatSize(archiveInfo.Size));
            _result.PackageName = name;
            _result.PackageDirectory = packageDirectory;
            _result.Reference = newReference;

            pruneRow.Name = $"Deleting old {prefix} publishes from {preparer.SharedRoot} (keeping the last {KeepReferences})";
            pruneRow.Start();
            var deleted = await Task.Run(() => preparer.PruneReferences(prefix, KeepReferences, pruneRow.AppendLine, ct), ct);
            _result.Pruned.AddRange(deleted);
            pruneRow.Finish(StepState.Succeeded, deleted.Count == 0
                ? "nothing to delete"
                : $"{deleted.Count} archive(s) deleted: {string.Join(", ", deleted)}");
        }
        catch (OperationCanceledException)
        {
            FinishUnfinished(StepState.Cancelled, null);
        }
        catch (Exception ex)
        {
            _hasError = true;
            Logger.LogError($"Preparing the deploy{(name is null ? string.Empty : $" {name}")} failed:\n{ex}");
            FinishUnfinished(StepState.Failed, ex.Message);
        }
        finally
        {
            DeployPreparer.TryDelete(deployDirectory);
            DeployPreparer.TryDelete(uploading);
            DeployPreparer.TryDelete(work);
        }

        void FinishUnfinished(StepState state, string? detail)
        {
            foreach (var row in step.Rows.Where(r => !r.IsFinished))
            {
                row.Finish(state, detail);
            }
        }
    }

    private static async Task<string?> DownloadAsync(IStep downloadRow, string? previousReference, string workDirectory, CancellationToken ct)
    {
        if (previousReference is null)
        {
            downloadRow.Finish(StepState.Succeeded, "no previous publish of this branch in the shared directory, everything is new");
            return null;
        }

        downloadRow.Name = $"Downloading {Path.GetFileName(previousReference)} and extracting it into {workDirectory}";
        downloadRow.Start();
        var (previous, files) = await Task.Run(() => DeployPreparer.Download(previousReference, workDirectory, downloadRow.AppendLine, ct), ct);
        downloadRow.Finish(StepState.Succeeded, $"{files} files");
        return previous;
    }

    private static void PlaceManifest(string preparedManifest, string packageDirectory, string newReference)
    {
        try
        {
            DeployPreparer.PlaceManifest(preparedManifest, packageDirectory);
        }
        catch (Exception e)
        {
            Logger.LogError($"The manifest could not be placed into {packageDirectory}, removing the uploaded reference {newReference}:\n{e}");
            DeployPreparer.TryDelete(newReference);
            throw;
        }
    }

    private async Task<bool> BuildDependencies(string prefix, CancellationToken ct)
    {
        string traversalProject = DependencyBuild.WriteTraversalProject(_projects, _configuration, prefix);
        try
        {
            return await RunMsBuild(DependencyBuild.BuildCommand(traversalProject), _buildRow, ct);
        }
        finally
        {
            DependencyBuild.TryDelete(traversalProject);
        }
    }

    private async Task<bool> PublishProject(PublishCommandBuilder command, string prefix, IStep row, CancellationToken ct)
    {
        try
        {
            if (Directory.Exists(command.PublishDir))
            {
                Directory.Delete(command.PublishDir, recursive: true);
            }
        }
        catch (Exception ex)
        {
            row.Finish(StepState.Failed, $"could not clear {command.PublishDir}: {ex.Message}");
            _hasError = true;
            Logger.LogError(ex.ToString());
            return false;
        }

        return await RunMsBuild(command.BuildCommand(prefix), row, ct);
    }

    private async Task<bool> RunMsBuild(string arguments, IStep row, CancellationToken ct)
    {
        row.Start();
        try
        {
            var result = await ProcessHelper.RunAsync("MSBuild", arguments, onOutputLine: row.AppendLine, cancellationToken: ct);
            if (result.ExitCode == 0)
            {
                row.Finish(StepState.Succeeded);
                return true;
            }

            row.Finish(StepState.Failed);
            _hasError = true;
            Logger.LogError(string.Join('\n', result.Output, result.Error));
        }
        catch (OperationCanceledException)
        {
            row.Finish(StepState.Cancelled);
        }
        catch (Exception ex)
        {
            row.Finish(StepState.Failed, ex.Message);
            _hasError = true;
            Logger.LogError(ex.ToString());
        }

        return false;
    }
}
