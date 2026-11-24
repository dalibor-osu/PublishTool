using PublishTool;
using PublishTool.Commands;
using PublishTool.Configuration;
using PublishTool.Generated;
using PublishTool.GitHub;
using PublishTool.Helpers;
using Spectre.Console;

using var ct = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    if (ct.IsCancellationRequested)
    {
        e.Cancel = CriticalSection.IsActive;
        return;
    }

    ct.Cancel(false);
    e.Cancel = true;
};

SelfUpdate.CleanUpBackup();

if (RemovedSwitches.Find(args) is { } removed)
{
    AnsiConsole.MarkupLineInterpolated($"[red]{removed}[/]");
    return 1;
}

int returnCode = 0;
var commandResult = CommandParsers.Parse(args);
if (!commandResult.IsSuccess)
{
    AnsiConsole.MarkupLine($"[red]An error occurred when running the command:[/] {commandResult.Error}");
    Logger.Write(Console.WriteLine);
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
    catch (OperationCanceledException)
    {
        return ct.IsCancellationRequested ? 0 : 1;
    }
    catch (ConfigException e)
    {
        AnsiConsole.MarkupLineInterpolated($"[red]{e.Message}[/]");
        Logger.LogError(e.ToString());
        return 1;
    }
    catch (Exception e)
    {
        Logger.LogError(e.ToString());
        return 1;
    }
};

if (commandResult.Value is IAlternateScreenCommand { UsesAlternateScreen: true })
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

Logger.Write(Console.WriteLine);

return returnCode;
