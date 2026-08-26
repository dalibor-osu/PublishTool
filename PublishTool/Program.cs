using System.Diagnostics;
using PublishTool;
using Spectre.Console;

if (!await CheckMsBuild()) {
  AnsiConsole.MarkupLine("[red]MSBuild not found! Make sure it is installed and in your PATH environment variable before running this tool.[/]");
  return 1;
}

string currentWorkingDirectory = args.Length > 0 ? args[0] : Directory.GetCurrentDirectory();

ConfigHandler.Load(currentWorkingDirectory);

var projects = ProjectScanner.Scan(currentWorkingDirectory);

if (projects.Count == 0) {
  AnsiConsole.MarkupLine("No projects found in this directory.");
  return 1;
}

var selectedProjects = await AnsiConsole.PromptAsync(
  new MultiSelectionPrompt<string>()
    .Title("Select [green]projects[/] to publish:")
    .AddChoices(projects.Select(p => p.Name)));

var projectsToPublish = projects.Where(p => selectedProjects.Any(sp => p.AbsolutePath.EndsWith($"{sp}.csproj"))).ToList();

AnsiConsole.Clear();

string configuration = await AnsiConsole.PromptAsync(
  new SelectionPrompt<string>()
    .Title("Select build Configuration:")
    .AddChoices("Release", "Debug"));

var publishCommands = projectsToPublish
  .Select(p => new PublishCommand(p, configuration, Path.Combine(ConfigHandler.Instance.PublishDirectories[currentWorkingDirectory], p.Name))).ToList();

await AnsiConsole.Progress()
  .AutoRefresh(true)
  .AutoClear(false)
  .HideCompleted(false)
  .Columns(
    new SpinnerColumn(Spinner.Known.Ascii),
    new TaskDescriptionColumn { Alignment = Justify.Left },
    new ElapsedTimeColumn())
  .StartAsync(async ctx => {
    var work = publishCommands.Select(c => new {
      Name = $"Publishing {c.Project.Name}",
      Job = new Func<Task<(int ExitCode, string Output, string Error)>>(() => RunAsync("MSBuild", c.BuildCommand()))
    });

    var running = work.Select(async item => {
      var task = ctx.AddTask(item.Name, new ProgressTaskSettings { AutoStart = true });
      task.IsIndeterminate = true;

      try {
        var result = await item.Job();
        if (result.ExitCode == 0) {
          task.Description = $"[green]{item.Name} - done[/]";
        } else {
          task.Description = $"[red]{item.Name} - failed[/]";
        }
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

static async Task<bool> CheckMsBuild() {
  return await RunAsync("MSBuild", "/version").ContinueWith(t => t.Result.ExitCode == 0);
}

static async Task<(int ExitCode, string Output, string Error)> RunAsync(
  string file,
  string arguments,
  string? workingDirectory = null,
  CancellationToken cancellationToken = default) {
  var psi = new ProcessStartInfo {
    FileName = file,
    Arguments = arguments,
    WorkingDirectory = workingDirectory ?? Environment.CurrentDirectory,
    RedirectStandardOutput = true,
    RedirectStandardError = true,
    UseShellExecute = false,
    CreateNoWindow = true,
  };

  using var process = new Process();
  process.StartInfo = psi;
  process.Start();

  var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
  var stderr = process.StandardError.ReadToEndAsync(cancellationToken);

  await process.WaitForExitAsync(cancellationToken);

  return (process.ExitCode, await stdout, await stderr);
}