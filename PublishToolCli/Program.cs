using System.Text.Json;
using PublishTool;
using PublishTool.GitHub;
using PublishTool.Helpers;
using PublishToolCli;
using PublishToolCli.Generated;
using PublishToolCli.Output;

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    if (cancellation.IsCancellationRequested)
    {
        e.Cancel = CriticalSection.IsActive;
        return;
    }

    cancellation.Cancel(false);
    e.Cancel = true;
};

SelfUpdate.CleanUpBackup();

var parsed = CommandParsers.TryParse(args);
if (parsed is null)
{
    string error = args.Length == 0 ? "no command given" : $"unknown command '{args[0]}'";
    await Console.Error.WriteLineAsync($"error: {error}");
    await Console.Error.WriteLineAsync();
    await Console.Error.WriteLineAsync(CommandParsers.ApplicationHelp);
    await WriteJsonErrorAsync(error);
    return ExitCodes.InvalidArguments;
}

if (!parsed.IsSuccess)
{
    await Console.Error.WriteLineAsync($"error: {parsed.Error}");
    await Console.Error.WriteLineAsync("Run 'PublishToolCli <command> --help' for the options of a command.");
    await WriteJsonErrorAsync(parsed.Error);
    return ExitCodes.InvalidArguments;
}

int exitCode;
try
{
    exitCode = await parsed.Value.ExecuteAsync(cancellation.Token);
}
catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
{
    exitCode = ExitCodes.Cancelled;
}
catch (Exception e)
{
    Logger.LogError(e.ToString());
    await Console.Error.WriteLineAsync($"error: {e.Message}");
    exitCode = ExitCodes.Failed;
}

Logger.Write(Console.Error.WriteLine);
return exitCode;

async Task WriteJsonErrorAsync(string error)
{
    if (!args.Contains("--json"))
    {
        return;
    }

    var result = new CliResult(args.Length > 0 && !args[0].StartsWith('-') ? args[0] : string.Empty)
    {
        Status = ExitCodes.Status(ExitCodes.InvalidArguments),
        ExitCode = ExitCodes.InvalidArguments,
        Errors = [error]
    };
    await Console.Out.WriteLineAsync(JsonSerializer.Serialize(result, CliJsonContext.Default.CliResult));
}
