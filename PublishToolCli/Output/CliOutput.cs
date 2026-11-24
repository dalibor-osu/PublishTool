using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using PublishTool;
using PublishTool.Configuration;
using PublishTool.Deployment;

namespace PublishToolCli.Output;

public sealed class CliOutput(bool json, bool verbose)
{
    private readonly Lock _lock = new();

    public bool Json { get; } = json;

    public bool Verbose { get; } = verbose;

    private TextWriter Info => Json ? System.Console.Error : System.Console.Out;

    public void Line(string text)
    {
        lock (_lock)
        {
            Info.WriteLine(text);
        }
    }

    public void Error(string text)
    {
        lock (_lock)
        {
            System.Console.Error.WriteLine($"error: {text}");
        }
    }

    public void Warning(CliWarning warning) =>
        Line($"warning [{warning.Code}]{(warning.Accepted ? " (accepted)" : string.Empty)}: {warning.Message}");

    public int Fail(CliResult result, int exitCode, string message)
    {
        Error(message);
        result.Errors.Add(message);
        return exitCode;
    }

    public static List<CliWarning> Warnings(IEnumerable<DeployWarning> warnings, IReadOnlyCollection<string> accepted) =>
    [
        .. warnings.Select(w => new CliWarning(w.Code, w.Message, accepted.Contains(w.Code, StringComparer.OrdinalIgnoreCase)))
    ];

    public async Task<int> RunAsync<TResult>(TResult result, JsonTypeInfo<TResult> typeInfo, Func<Task<int>> body, CancellationToken ct)
        where TResult : CliResult
    {
        int code = await RunBodyAsync(result, body, ct);
        result.ExitCode = code;
        result.Status = ExitCodes.Status(code);
        if (Json)
        {
            lock (_lock)
            {
                System.Console.Out.WriteLine(JsonSerializer.Serialize(result, typeInfo));
            }
        }
        else if (code != ExitCodes.Success)
        {
            Line($"Finished with exit code {code} ({result.Status}).");
        }

        return code;
    }

    private async Task<int> RunBodyAsync(CliResult result, Func<Task<int>> body, CancellationToken ct)
    {
        try
        {
            return await body();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return Fail(result, ExitCodes.Cancelled, "Cancelled");
        }
        catch (Exception e) when (e is CliArgumentException or ConfigException)
        {
            Logger.LogError(e.ToString());
            return Fail(result, ExitCodes.InvalidArguments, e.Message);
        }
        catch (Exception e) when (e is DeployException)
        {
            Logger.LogError(e.ToString());
            return Fail(result, ExitCodes.Failed, e.Message);
        }
        catch (Exception e)
        {
            Logger.LogError(e.ToString());
            return Fail(result, ExitCodes.Failed, $"Unexpected error: {e.Message}");
        }
    }
}
