using PublishTool.Attributes;
using PublishTool.Commands.Options;
using PublishTool.Console;
using PublishTool.Helpers;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace PublishTool.Commands;

[Command("publish", IsDefault = true, Description = "Publish the projects in a directory")]
public class PublishCommand(PublishCommandOptions options) : ICommand<PublishCommandOptions>
{
    private const int OutputTailLength = 5;
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
        var config = ConfigHandler.Load(currentWorkingDirectory);
        var projects = ProjectScanner.Scan(config, currentWorkingDirectory);

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

        string publishRoot = config.PublishDirectories[currentWorkingDirectory];
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
                config.SharedDirectories[currentWorkingDirectory],
                config.DeployDirectories[currentWorkingDirectory],
                publishRoot,
                projectsToPublish.Select(p => p.Name).ToList());
            deployStep = new DeployStep(
                preparer,
                display.Add("Collecting changed files into the deploy directory"),
                display.Add("Copying the full publish to the shared directory"));
        }

        var work = PublishAll(projectsToPublish, configuration, buildRow, publishRows, deployStep, ct);
        AnsiConsole.Cursor.Hide();
        try
        {
            // Redraw on a timer regardless of cancellation so the display keeps up until the very end.
            while (await Task.WhenAny(work, Task.Delay(RefreshInterval)) != work)
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

    private sealed record DeployStep(DeployPreparer Preparer, JobRow ChangesRow, JobRow UploadRow);

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
        deployStep.ChangesRow.Finish(state, reason);
        deployStep.UploadRow.Finish(state, reason);
    }

    private async Task PrepareDeploy(DeployStep step, CancellationToken ct)
    {
        var (preparer, changesRow, uploadRow) = step;
        string? name = null;
        string? deployDirectory = null;
        string? uploading = null;
        try
        {
            string prefix = await DeployPreparer.ResolvePrefixAsync(Options.WorkingDirectory, ct);
            name = DeployPreparer.DirectoryName(prefix, DateTimeOffset.Now);
            string? previous = preparer.FindPreviousReference(prefix);

            deployDirectory = preparer.DeployPath(name);
            changesRow.Name = previous is null
                ? $"Collecting changed files into {deployDirectory} (no previous publish in the shared directory, everything is new)"
                : $"Collecting changed files into {deployDirectory} (compared with {Path.GetFileName(previous)})";
            changesRow.Start();
            int changed = await Task.Run(() => preparer.CollectChanges(deployDirectory, previous, changesRow.AppendLine, ct), ct);
            changesRow.Finish(JobState.Succeeded, $"{changed} files");
            deployDirectory = null;

            string reference = preparer.ReferencePath(name);
            uploadRow.Name = $"Copying the full publish to {reference}";
            uploadRow.Start();
            uploading = await Task.Run(() => preparer.Upload(reference, uploadRow.AppendLine, ct), ct);
            var carried = previous is null
                ? []
                : await Task.Run(() => preparer.CarryForwardMissingProjects(uploading, previous, uploadRow.AppendLine, ct), ct);
            DeployPreparer.Commit(uploading, reference);
            uploading = null;
            uploadRow.Finish(JobState.Succeeded, carried.Count == 0
                ? null
                : $"{carried.Count} unpublished project(s) carried over from {Path.GetFileName(previous)}");
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
        }

        void FinishUnfinished(JobState state, string? detail)
        {
            foreach (var row in new[] { changesRow, uploadRow }.Where(r => !r.IsFinished))
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
