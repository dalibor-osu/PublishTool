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

        var publishCommands = projectsToPublish
            .Select(p => new PublishCommandBuilder(p, configuration,
                Path.Combine(config.PublishDirectories[currentWorkingDirectory], p.Name))).ToList();

        var display = new JobDisplay(OutputTailLength);
        var buildRow = display.Add("Building projects and their dependencies");
        var publishRows = publishCommands
            .Select(c => (Command: c, Row: display.Add($"Publishing {c.Project.Name}")))
            .ToList();

        var work = PublishAll(projectsToPublish, configuration, buildRow, publishRows, ct);
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

    private async Task PublishAll(
        IReadOnlyList<DotnetProject> projects,
        string configuration,
        JobRow buildRow,
        List<(PublishCommandBuilder Command, JobRow Row)> publishRows,
        CancellationToken ct)
    {
        bool built = await BuildDependencies(projects, configuration, buildRow, ct);
        if (!built)
        {
            foreach (var (_, row) in publishRows)
            {
                row.Finish(ct.IsCancellationRequested ? JobState.Cancelled : JobState.Skipped, "build failed");
            }

            return;
        }

        await Task.WhenAll(publishRows.Select(item => RunMsBuild(item.Command.BuildCommand(), item.Row, ct)));
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
