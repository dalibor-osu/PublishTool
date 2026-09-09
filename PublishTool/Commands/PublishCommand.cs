using PublishTool.Attributes;
using PublishTool.Commands.Options;
using PublishTool.Configuration;
using PublishTool.Console;
using PublishTool.Helpers;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace PublishTool.Commands;

[Command("publish", IsDefault = true, Description = "Publish the projects in a directory")]
public class PublishCommand(PublishCommandOptions options) : ICommand<PublishCommandOptions>
{
    private const int OutputTailLength = 5;
    private const int KeepReferences = 3;
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMilliseconds(100);

    public PublishCommandOptions Options { get; } = options;

    public bool UsesAlternateScreen => true;

    private volatile bool _hasError;

    public async Task<int> ExecuteAsync(CancellationToken ct)
    {
        if (!await CheckMsBuild())
        {
            AnsiConsole.MarkupLine(
                "[red]MSBuild not found! Make sure it is installed and in your PATH environment variable before running this tool.[/]");
            return 1;
        }

        string currentWorkingDirectory = Options.WorkingDirectory;
        var settings = ConfigHandler.Load(currentWorkingDirectory);
        if (!settings.IsComplete)
        {
            throw new ConfigException($"The directory {currentWorkingDirectory} is not fully configured, run 'PublishTool config'.");
        }

        var projects = ProjectScanner.Scan(settings, currentWorkingDirectory);

        if (projects.Count == 0)
        {
            AnsiConsole.MarkupLine("No projects found in this directory.");
            return 1;
        }

        var selectedProjects = Options.All
            ? projects.Select(p => p.Name)
            : await AnsiConsole.PromptAsync(
                new MultiSelectionPrompt<string>()
                    .Title("Select [green]projects[/] to publish:")
                    .AddChoices(projects.Select(p => p.Name)), ct);

        var projectsToPublish = projects.Where(p => selectedProjects.Any(sp => p.AbsolutePath.EndsWith($"{sp}.csproj"))).ToList();

        string configuration = Options.All
            ? "Release"
            : await AnsiConsole.PromptAsync(
                new SelectionPrompt<string>()
                    .Title("Select build Configuration:")
                    .AddChoices("Release", "Debug"), ct);

        AnsiConsole.Clear();

        string publishRoot = settings.PublishDirectory;
        var publishCommands = projectsToPublish
            .Select(p => new PublishCommandBuilder(p, configuration, Path.Combine(publishRoot, p.Name))).ToList();

        var display = new JobDisplay(OutputTailLength);
        var buildRow = display.Add("Building projects and their dependencies");
        var publishRows = publishCommands
            .Select(c => (Command: c, Row: display.Add($"Publishing {c.Project.Name}")))
            .ToList();

        DeployStep? deployStep = null;
        if (Options.Complete)
        {
            var preparer = new DeployPreparer(
                settings.SharedDirectory,
                settings.DeployDirectory,
                publishRoot,
                projectsToPublish.Select(p => p.Name).ToList());
            deployStep = new DeployStep(
                preparer,
                display.Add("Downloading the previous publish of this branch from the shared directory"),
                display.Add("Collecting changed files into the deploy directory"),
                display.Add("Compressing the full publish into an archive"),
                display.Add("Uploading the archive to the shared directory"),
                display.Add($"Deleting old publishes of this branch from the shared directory (keeping the last {KeepReferences})"));
        }

        var work = PublishAll(projectsToPublish, configuration, buildRow, publishRows, deployStep, ct);
        AnsiConsole.Cursor.Hide();
        try
        {
            // Redraw on a timer regardless of cancellation so the display keeps up until the very end.
            while (await Task.WhenAny(work, Task.Delay(RefreshInterval, CancellationToken.None)) != work)
            {
                Draw(display);
            }

            await work;
            display.Stop();
            Draw(display);
        }
        finally
        {
            AnsiConsole.Cursor.Show();
        }

        return _hasError ? 1 : 0;
    }

    private static readonly string Escape = ((char)27).ToString();
    private static readonly ControlCode CursorHome = new(Escape + "[H");
    private static readonly ControlCode EraseBelow = new(Escape + "[J");

    private static void Draw(JobDisplay display)
    {
        AnsiConsole.Write(CursorHome);
        AnsiConsole.Write(display);
        AnsiConsole.Write(EraseBelow);
    }

    private sealed record DeployStep(
        DeployPreparer Preparer,
        JobRow DownloadRow,
        JobRow ChangesRow,
        JobRow ArchiveRow,
        JobRow UploadRow,
        JobRow PruneRow)
    {
        public IEnumerable<JobRow> Rows => [DownloadRow, ChangesRow, ArchiveRow, UploadRow, PruneRow];
    }

    private async Task PublishAll(
        IReadOnlyList<DotnetProject> projects,
        string configuration,
        JobRow buildRow,
        List<(PublishCommandBuilder Command, JobRow Row)> publishRows,
        DeployStep? deployStep,
        CancellationToken ct)
    {
        bool built = await BuildDependencies(projects, configuration, buildRow, ct);
        if (!built)
        {
            foreach (var (_, row) in publishRows)
            {
                row.Finish(ct.IsCancellationRequested ? JobState.Cancelled : JobState.Skipped, "build failed");
            }

            SkipDeploy(deployStep, "build failed", ct);
            return;
        }

        await Task.WhenAll(publishRows.Select(item => RunMsBuild(item.Command.BuildCommand(), item.Row, ct)));

        if (deployStep is null)
        {
            return;
        }

        if (_hasError || ct.IsCancellationRequested)
        {
            SkipDeploy(deployStep, "publish failed", ct);
            return;
        }

        await PrepareDeploy(deployStep, ct);
    }

    private static void SkipDeploy(DeployStep? deployStep, string reason, CancellationToken ct)
    {
        if (deployStep is null)
        {
            return;
        }

        var state = ct.IsCancellationRequested ? JobState.Cancelled : JobState.Skipped;
        foreach (var row in deployStep.Rows)
        {
            row.Finish(state, reason);
        }
    }

    private async Task PrepareDeploy(DeployStep step, CancellationToken ct)
    {
        var (preparer, downloadRow, changesRow, archiveRow, uploadRow, pruneRow) = step;
        string? name = null;
        string? work = null;
        string? deployDirectory = null;
        string? uploading = null;
        try
        {
            string prefix = await DeployPreparer.ResolvePrefixAsync(Options.WorkingDirectory, ct);
            name = DeployPreparer.DirectoryName(prefix, DateTimeOffset.Now);
            string? previousReference = preparer.FindPreviousReference(prefix);
            work = preparer.WorkPath(name);

            string? previous = null;
            if (previousReference is null)
            {
                downloadRow.Finish(JobState.Succeeded, "no previous publish of this branch in the shared directory, everything is new");
            }
            else
            {
                string reference = previousReference;
                string workDirectory = work;
                downloadRow.Name = $"Downloading {Path.GetFileName(reference)} and extracting it into {workDirectory}";
                downloadRow.Start();
                (previous, int files) = await Task.Run(() => DeployPreparer.Download(reference, workDirectory, downloadRow.AppendLine, ct), ct);
                downloadRow.Finish(JobState.Succeeded, $"{files} files");
            }

            deployDirectory = preparer.DeployPath(name);
            changesRow.Name = previousReference is null
                ? $"Collecting changed files into {deployDirectory} (everything is new)"
                : $"Collecting changed files into {deployDirectory} (compared with {Path.GetFileName(previousReference)})";
            changesRow.Start();
            int changed = await Task.Run(() => preparer.CollectChanges(deployDirectory, previous, changesRow.AppendLine, ct), ct);
            changesRow.Finish(JobState.Succeeded, $"{changed} files");
            deployDirectory = null;

            string archive = preparer.ArchivePath(name);
            archiveRow.Name = $"Compressing the full publish into {archive}";
            archiveRow.Start();
            var archiveInfo = await Task.Run(() => preparer.CreateArchive(archive, previous, archiveRow.AppendLine, ct), ct);
            archiveRow.Finish(JobState.Succeeded, archiveInfo.Carried.Count == 0
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
            uploadRow.Finish(JobState.Succeeded, DeployPreparer.FormatSize(archiveInfo.Size));

            pruneRow.Name = $"Deleting old {prefix} publishes from {preparer.SharedRoot} (keeping the last {KeepReferences})";
            pruneRow.Start();
            var deleted = await Task.Run(() => preparer.PruneReferences(prefix, KeepReferences, pruneRow.AppendLine, ct), ct);
            pruneRow.Finish(JobState.Succeeded, deleted.Count == 0
                ? "nothing to delete"
                : $"{deleted.Count} archive(s) deleted: {string.Join(", ", deleted)}");
        }
        catch (OperationCanceledException)
        {
            FinishUnfinished(JobState.Cancelled, null);
        }
        catch (Exception ex)
        {
            _hasError = true;
            Logger.LogError($"Preparing the deploy{(name is null ? string.Empty : $" {name}")} failed:\n{ex}");
            FinishUnfinished(JobState.Failed, ex.Message);
        }
        finally
        {
            DeployPreparer.TryDelete(deployDirectory);
            DeployPreparer.TryDelete(uploading);
            DeployPreparer.TryDelete(work);
        }

        void FinishUnfinished(JobState state, string? detail)
        {
            foreach (var row in step.Rows.Where(r => !r.IsFinished))
            {
                row.Finish(state, detail);
            }
        }
    }

    private async Task<bool> BuildDependencies(IReadOnlyList<DotnetProject> projects, string configuration, JobRow row, CancellationToken ct)
    {
        string traversalProject = DependencyBuild.WriteTraversalProject(projects, configuration);
        try
        {
            return await RunMsBuild(DependencyBuild.BuildCommand(traversalProject), row, ct);
        }
        finally
        {
            DependencyBuild.TryDelete(traversalProject);
        }
    }

    private async Task<bool> RunMsBuild(string arguments, JobRow row, CancellationToken ct)
    {
        row.Start();
        try
        {
            var result = await ProcessHelper.RunAsync("MSBuild", arguments, onOutputLine: row.AppendLine, cancellationToken: ct);
            if (result.ExitCode == 0)
            {
                row.Finish(JobState.Succeeded);
                return true;
            }

            row.Finish(JobState.Failed);
            _hasError = true;
            Logger.LogError(string.Join('\n', result.Output, result.Error));
        }
        catch (OperationCanceledException)
        {
            row.Finish(JobState.Cancelled);
        }
        catch (Exception ex)
        {
            row.Finish(JobState.Failed, ex.Message);
            _hasError = true;
            Logger.LogError(ex.ToString());
        }

        return false;
    }

    private static async Task<bool> CheckMsBuild()
    {
        try
        {
            return await ProcessHelper.RunAsync("MSBuild", "/version").ContinueWith(t => t.Result.ExitCode == 0);
        }
        catch (Exception e)
        {
            Logger.LogError(e.ToString());
            return false;
        }
    }
}
