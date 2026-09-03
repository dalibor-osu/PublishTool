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

        string? deployRoot = Options.Complete ? ConfigHandler.EnsureDeployDirectory(config, currentWorkingDirectory) : null;

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
        if (deployRoot is not null)
        {
            deployStep = new DeployStep(
                new DeployPreparer(deployRoot, publishRoot, projectsToPublish.Select(p => p.Name).ToList()),
                display.Add("Preparing the deploy directory locally"),
                display.Add("Collecting changed files"),
                display.Add("Copying the deploy directory to the deploy root"));
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

    private sealed record DeployStep(DeployPreparer Preparer, JobRow CopyRow, JobRow ChangesRow, JobRow UploadRow);

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
        deployStep.CopyRow.Finish(state, reason);
        deployStep.ChangesRow.Finish(state, reason);
        deployStep.UploadRow.Finish(state, reason);
    }

    private async Task PrepareDeploy(DeployStep step, CancellationToken ct)
    {
        var (preparer, copyRow, changesRow, uploadRow) = step;
        string? target = null;
        string? stage = null;
        string? uploading = null;
        try
        {
            string prefix = await DeployPreparer.ResolvePrefixAsync(Options.WorkingDirectory, ct);
            target = preparer.TargetPath(prefix, DateTimeOffset.Now);
            string targetName = Path.GetFileName(target);
            string? previous = preparer.FindPreviousDeploy(prefix);
            stage = DeployPreparer.CreateStagingDirectory();

            copyRow.Name = $"Preparing {targetName} in a local temp directory";
            copyRow.Start();
            int copied = await Task.Run(() => preparer.CopyPublishedProjects(stage, copyRow.AppendLine, ct), ct);
            copyRow.Finish(JobState.Succeeded, $"{copied} files");

            string changesDir = $"{targetName}\\{DeployPreparer.ChangesDirectoryName}";
            changesRow.Name = previous is null
                ? $"Collecting changed files into {changesDir} (no previous deploy, everything is new)"
                : $"Collecting changed files into {changesDir} (compared with {Path.GetFileName(previous)})";
            changesRow.Start();
            int changed = await Task.Run(() => preparer.CollectChanges(stage, previous, changesRow.AppendLine, ct), ct);
            changesRow.Finish(JobState.Succeeded, $"{changed} files");

            uploadRow.Name = $"Copying {targetName} to {preparer.DeployRoot}";
            uploadRow.Start();
            uploading = await Task.Run(() => preparer.Upload(stage, target, uploadRow.AppendLine, ct), ct);
            var carried = previous is null
                ? []
                : await Task.Run(() => preparer.CarryForwardMissingProjects(uploading, previous, uploadRow.AppendLine, ct), ct);
            DeployPreparer.Commit(uploading, target);
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
            Logger.LogError($"Preparing the deploy directory{(target is null ? string.Empty : $" {target}")} failed:\n{ex}");
            FinishUnfinished(JobState.Failed, ex.Message);
        }
        finally
        {
            DeployPreparer.TryDelete(uploading);
            DeployPreparer.TryDelete(stage);
        }

        void FinishUnfinished(JobState state, string? detail)
        {
            foreach (var row in new[] { copyRow, changesRow, uploadRow }.Where(r => !r.IsFinished))
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
