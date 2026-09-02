using PublishTool;
using PublishTool.Commands;
using PublishTool.Generated;
using Spectre.Console;

using var ct = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    if (ct.IsCancellationRequested)
    {
        return;
    }

    ct.Cancel(false);
    e.Cancel = true;
};

UpdateCommand.CleanUpBackup();

int returnCode = 0;
var commandResult = CommandParsers.Parse(args);
if (!commandResult.IsSuccess)
{
    AnsiConsole.MarkupLine($"[red]An error occurred when running the command:[/] {commandResult.Error}");
    Logger.Write();
    return 1;
}

if (commandResult.Value is VersionCommand versionCommand)
{
    _ = versionCommand.ExecuteAsync(ct.Token);
    return 0;
}

var commandAction = async Task<int> () =>
{
    try
    {
        return await commandResult.Value.ExecuteAsync(ct.Token);
    }
    catch (TaskCanceledException)
    {
        return ct.IsCancellationRequested ? 0 : 1;
    }
    catch (Exception e)
    {
        Logger.LogError(e.ToString());
        return 1;
    }
};

if (commandResult.Value.UsesAlternateScreen)
{
    AnsiConsole.AlternateScreen(() => Task.Run(async () =>
    {
        returnCode = await commandAction();

        AnsiConsole.MarkupLine(returnCode == 0
            ? "[green]Command finished successfully, press any key to exit...[/]"
            : "[red]Command failed, press any key to exit...[/]");
        AnsiConsole.Console.Input.ReadKey(true);
    }).Wait());
}
else
{
    returnCode = await commandAction();
}

Logger.Write();

return returnCode;