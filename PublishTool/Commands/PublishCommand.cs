using PublishTool.Console;
using PublishTool.Helpers;
using Spectre.Console;

namespace PublishTool.Commands;

public class PublishCommand(PublishCommand.Options options) : Command<PublishCommand.Options>(options) {
  public new class Options {
    public string WorkingDirectory { get; set; } = Directory.GetCurrentDirectory();
    public bool All { get; set; } = false;
  }

  public override async Task<int> ExecuteAsync(CancellationToken ct) {
    string currentWorkingDirectory = base.Options.WorkingDirectory;
    var projects = ProjectScanner.Scan(currentWorkingDirectory);

    if (projects.Count == 0) {
      AnsiConsole.MarkupLine("No projects found in this directory.");
      return 1;
    }


    var selectedProjects = base.Options.All ? projects.Select(p => p.Name) : await AnsiConsole.PromptAsync(
      new MultiSelectionPrompt<string>()
        .Title("Select [green]projects[/] to publish:")
        .AddChoices(projects.Select(p => p.Name)), ct);

    var projectsToPublish = projects.Where(p => selectedProjects.Any(sp => p.AbsolutePath.EndsWith($"{sp}.csproj"))).ToList();

    string configuration = base.Options.All ? "Release" : await AnsiConsole.PromptAsync(
      new SelectionPrompt<string>()
        .Title("Select build Configuration:")
        .AddChoices("Release", "Debug"), ct);

    AnsiConsole.Clear();

    var publishCommands = projectsToPublish
      .Select(p => new PublishCommandBuilder(p, configuration,
        Path.Combine(ConfigHandler.Instance.PublishDirectories[currentWorkingDirectory], p.Name))).ToList();

    await AnsiConsole.Progress()
      .AutoRefresh(true)
      .AutoClear(false)
      .HideCompleted(false)
      .Columns(
        new SpinnerColumn(Spinner.Known.Ascii),
        new ExpandingDescriptionColumn(13),
        new ElapsedTimeColumn())
      .StartAsync(async ctx => {
        var work = publishCommands.Select(c => new {
          Name = $"Publishing {c.Project.Name}",
          Job = new Func<Task<(int ExitCode, string Output, string Error)>>(() => ProcessHelper.RunAsync("MSBuild", c.BuildCommand(), cancellationToken: ct))
        });

        var running = work.Select(async item => {
          var task = ctx.AddTask(item.Name, new ProgressTaskSettings { AutoStart = true });
          task.IsIndeterminate = true;

          try {
            var result = await item.Job();
            task.Description = result.ExitCode == 0 ? $"[green]{item.Name} - done[/]" : $"[red]{item.Name} - failed[/]";
          } catch (Exception ex) {
            task.Description = $"[red]{item.Name} - failed: {ex.Message.EscapeMarkup()}[/]";
          } finally {
            task.IsIndeterminate = false;
            task.Value = task.MaxValue;
            task.StopTask();
          }
        });

        await Task.WhenAll(running);
      });

    return 0;
  }
}