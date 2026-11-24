using PublishTool.Configuration;
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
}
