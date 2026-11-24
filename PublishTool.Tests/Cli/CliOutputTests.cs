using System.Text.Json;
using PublishTool.Configuration;
using PublishTool.Deployment;
using PublishToolCli;
using PublishToolCli.Output;

namespace PublishTool.Tests.Cli;

public class CliOutputTests
{
    private static async Task<(int Code, CliResult Result)> Run(Func<Task<int>> body, bool cancelled = false)
    {
        using var cancellation = new CancellationTokenSource();
        if (cancelled)
        {
            await cancellation.CancelAsync();
        }

        var result = new VersionResult();
        int code = await new CliOutput(json: false, verbose: false).RunAsync(result, CliJsonContext.Default.VersionResult, body, cancellation.Token);
        return (code, result);
    }

    [Fact]
    public async Task RunAsync_SuccessAfterCancellation_StaysSuccess()
    {
        var (code, result) = await Run(() => Task.FromResult(ExitCodes.Success), cancelled: true);

        Assert.Equal(ExitCodes.Success, code);
        Assert.Equal("succeeded", result.Status);
    }

    [Fact]
    public async Task RunAsync_CancelledBody_IsExit130()
    {
        var (code, result) = await Run(() => throw new OperationCanceledException(), cancelled: true);

        Assert.Equal(ExitCodes.Cancelled, code);
        Assert.Equal("cancelled", result.Status);
    }

    [Theory]
    [InlineData("argument", ExitCodes.InvalidArguments)]
    [InlineData("config", ExitCodes.InvalidArguments)]
    [InlineData("other", ExitCodes.Failed)]
    public async Task RunAsync_MapsExceptionsToExitCodes(string kind, int expected)
    {
        Exception exception = kind switch
        {
            "argument" => new CliArgumentException("boom"),
            "config" => new ConfigException("boom"),
            _ => new InvalidOperationException("boom")
        };

        var (code, result) = await Run(() => throw exception);

        Assert.Equal(expected, code);
        Assert.Equal(expected, result.ExitCode);
        Assert.Contains("boom", Assert.Single(result.Errors));
    }

    [Fact]
    public void Warnings_CarryTheCompleteFileListIntoTheJson()
    {
        string[] files = [.. Enumerable.Range(1, DeployWarning.MaxListedFiles + 5).Select(i => $"Lib{i}.dll")];
        var result = new DeployCliResult
        {
            Warnings = CliOutput.Warnings(
                [
                    new DeployWarning(WarningCodes.TargetModified, "capped", files),
                    new DeployWarning(WarningCodes.ToolVersion, "no files")
                ],
                [WarningCodes.TargetModified])
        };

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(result, CliJsonContext.Default.DeployCliResult));

        var warnings = json.RootElement.GetProperty("warnings");
        Assert.True(warnings[0].GetProperty("accepted").GetBoolean());
        Assert.Equal(files, warnings[0].GetProperty("files").EnumerateArray().Select(f => f.GetString()));
        Assert.False(warnings[1].TryGetProperty("files", out _));
    }
}
