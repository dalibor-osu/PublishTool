using PublishTool;
using PublishTool.Helpers;
using Spectre.Console;

using var ct = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => {
  if (ct.IsCancellationRequested) {
    return;
  }

  ct.Cancel(false);
  e.Cancel = true;
};

if (!await CheckMsBuild()) {
  AnsiConsole.MarkupLine(
    "[red]MSBuild not found! Make sure it is installed and in your PATH environment variable before running this tool.[/]");
  return 1;
}

int returnCode = 0;
AnsiConsole.AlternateScreen(() => Task.Run(async () => {
  var commandResult = ArgumentParser.Parse(args);
  if (!commandResult.IsSuccess) {
    AnsiConsole.MarkupLine($"[red]An error occurred when running the command:[/] {commandResult.Error}");
    returnCode = 1;
    return;
  }

  try {
    returnCode = await commandResult.Value.ExecuteAsync(ct.Token);
  } catch (TaskCanceledException) {
    returnCode = ct.IsCancellationRequested ? 0 : 1;
    return;
  } catch (Exception e) {
    AnsiConsole.WriteLine(e.ToString());
    returnCode = 1;
    return;
  }

  AnsiConsole.MarkupLine(returnCode == 0
    ? "[green]Command finished successfully, press any key to exit...[/]"
    : "[red]Command failed, press any key to exit...[/]");
  AnsiConsole.Console.Input.ReadKey(true);
}).Wait());

return returnCode;

static Task<bool> CheckMsBuild() => ProcessHelper.RunAsync("MSBuild", "/version").ContinueWith(t => t.Result.ExitCode == 0);