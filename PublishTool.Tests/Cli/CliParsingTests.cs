using PublishTool.Deployment;
using PublishToolCli;
using PublishToolCli.Commands;
using PublishToolCli.Generated;

namespace PublishTool.Tests.Cli;

public class CliParsingTests
{
    [Fact]
    public void Deploy_ParsesTargetsServicesAndAcceptedCodes()
    {
        string target = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "web"));

        var parsed = CommandParsers.TryParse(
            ["deploy", "--target", $"Web={target}", "--service", "web=WebService", "--accept", "TOOL-VERSION,chain-mismatch", "--yes"]);

        var command = Assert.IsType<DeployCommand>(Assert.IsType<Result<PublishTool.Commands.ICommand>>(parsed).Value);
        var web = command.Options.Targets["WEB"];
        Assert.Equal(target, web.Path);
        Assert.True(web.ServiceSet);
        Assert.Equal("WebService", web.ServiceName);
        Assert.Equal([WarningCodes.ToolVersion, WarningCodes.ChainMismatch], command.Options.Accept);
        Assert.True(command.Options.Executes);
    }

    [Fact]
    public void Deploy_ParsesRepeatedSkipLists()
    {
        var command = Assert.IsType<DeployCommand>(CommandParsers.TryParse(["deploy", "--skip", "Api, Worker", "--skip", "Jobs"])!.Value);

        Assert.Equal(["Api", "Worker", "Jobs"], command.Options.Skip);
    }

    [Fact]
    public void Config_SkipAndRemoveSkip_ChangeTargets()
    {
        var command = Assert.IsType<ConfigCommand>(
            CommandParsers.TryParse(["config", "--prefix", "uat", "--skip", "Api", "--remove-skip", "Web"])!.Value);

        Assert.Equal(["Api"], command.Options.Skip);
        Assert.Equal(["Web"], command.Options.RemoveSkip);
        Assert.True(command.Options.ChangesTargets);
    }

    [Fact]
    public void Deploy_YesWithDryRun_DoesNotExecute()
    {
        var command = Assert.IsType<DeployCommand>(CommandParsers.TryParse(["deploy", "--yes", "--dry-run"])!.Value);

        Assert.False(command.Options.Executes);
    }

    [Theory]
    [InlineData("deploy", "--accept", "bogus", "Unknown warning code")]
    [InlineData("deploy", "--target", "Web=relative", "absolute path")]
    [InlineData("publish", "-c", "Fast", "Unknown configuration")]
    [InlineData("config", "--unset", "colour", "Unknown setting")]
    [InlineData("deploy", "--skip", " , ", "needs a project name")]
    public void InvalidValues_AreParseErrors(string command, string option, string value, string expected)
    {
        var parsed = CommandParsers.TryParse([command, option, value]);

        Assert.NotNull(parsed);
        Assert.False(parsed.IsSuccess);
        Assert.Contains(expected, parsed.Error);
    }

    [Fact]
    public void Publish_ParsesProjectsAndConfigurationCaseInsensitively()
    {
        var command = Assert.IsType<PublishCommand>(CommandParsers.TryParse(["publish", "-p", "Web, Api", "-c", "debug", "-l"])!.Value);

        Assert.Equal(["Web", "Api"], command.Options.Projects);
        Assert.Equal(BuildConfigurations.Debug, command.Options.Configuration);
        Assert.True(command.Options.Local);
    }

    [Fact]
    public void NoCommand_IsNotParsed()
    {
        Assert.Null(CommandParsers.TryParse([]));
        Assert.Null(CommandParsers.TryParse(["-dir", "x"]));
    }

    [Theory]
    [InlineData(ExitCodes.Success, "succeeded")]
    [InlineData(ExitCodes.InvalidArguments, "invalid-arguments")]
    [InlineData(ExitCodes.WarningsNotAccepted, "warnings-not-accepted")]
    [InlineData(ExitCodes.RestoreFailed, "restore-failed")]
    [InlineData(ExitCodes.Cancelled, "cancelled")]
    [InlineData(ExitCodes.Failed, "failed")]
    public void Status_NamesEveryExitCode(int code, string status)
    {
        Assert.Equal(status, ExitCodes.Status(code));
    }
}
