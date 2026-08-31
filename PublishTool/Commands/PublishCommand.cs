using PublishTool.Console;
using PublishTool.Helpers;
using Spectre.Console;

namespace PublishTool.Commands;

public class PublishCommand(PublishCommand.Options options) : Command<PublishCommand.Options>(options)
{
    public new class Options
    {
        public string WorkingDirectory { get; set; } = Directory.GetCurrentDirectory();
        public bool All { get; set; } = false;
    }

    public override bool UsesAlternateScreen => true;

    public override async Task<int> ExecuteAsync(CancellationToken ct)
    {
        if (!await CheckMsBuild())
        {
            AnsiConsole.MarkupLine(
                "[red]MSBuild not found! Make sure it is installed and in your PATH environment variable before running this tool.[/]");
            return 1;
        }

        string currentWorkingDirectory = base.Options.WorkingDirectory;
        var config = ConfigHandler.Load(currentWorkingDirectory);
        var projects = ProjectScanner.Scan(config, currentWorkingDirectory);

        if (projects.Count == 0)
        {
            AnsiConsole.MarkupLine("No projects found in this directory.");
            return 1;
        }


        var selectedProjects = base.Options.All
            ? projects.Select(p => p.Name)
            : await AnsiConsole.PromptAsync(
                new MultiSelectionPrompt<string>()
                    .Title("Select [green]projects[/] to publish:")
                    .AddChoices(projects.Select(p => p.Name)), ct);

        var projectsToPublish = projects.Where(p => selectedProjects.Any(sp => p.AbsolutePath.EndsWith($"{sp}.csproj"))).ToList();

        string configuration = base.Options.All
            ? "Release"
            : await AnsiConsole.PromptAsync(
                new SelectionPrompt<string>()
                    .Title("Select build Configuration:")
                    .AddChoices("Release", "Debug"), ct);

        AnsiConsole.Clear();

        var publishCommands = projectsToPublish
            .Select(p => new PublishCommandBuilder(p, configuration,
                Path.Combine(config.PublishDirectories[currentWorkingDirectory], p.Name))).ToList();

        bool hasError = false;
        await AnsiConsole.Progress()
            .AutoRefresh(true)
            .AutoClear(false)
            .HideCompleted(false)
            .Columns(
                new SpinnerColumn(Spinner.Known.Ascii),
                new ExpandingDescriptionColumn(13),
                new ElapsedTimeColumn())
            .StartAsync(async ctx =>
            {
                var work = publishCommands.Select(c => new
                {
                    Name = $"Publishing {c.Project.Name}",
                    Job = new Func<Task<(int ExitCode, string Output, string Error)>>(() =>
                        ProcessHelper.RunAsync("MSBuild", c.BuildCommand(), cancellationToken: ct))
                });

                var running = work.Select(async item =>
                {
                    var task = ctx.AddTask(item.Name, new ProgressTaskSettings { AutoStart = true });
                    task.IsIndeterminate = true;

                    try
                    {
                        var result = await item.Job();
                        if (result.ExitCode == 0)
                        {
                            task.Description = $"[green]{item.Name} - done[/]";
                        }
                        else
                        {
                            task.Description = $"[red]{item.Name} - failed[/]";
                            Logger.LogError(string.Join('\n', result.Output, result.Error));
                        }
                    }
                    catch (Exception ex)
                    {
                        task.Description = $"[red]{item.Name} - failed: {ex.Message.EscapeMarkup()}[/]";
                        hasError = true;
                        Logger.LogError(ex.ToString());
                    }
                    finally
                    {
                        task.IsIndeterminate = false;
                        task.Value = task.MaxValue;
                        task.StopTask();
                    }
                });

                await Task.WhenAll(running);
            });

        return hasError ? 1 : 0;
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