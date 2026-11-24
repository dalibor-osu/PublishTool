using System.Text.Json;
using PublishTool;
using PublishTool.Attributes;
using PublishTool.Commands;
using PublishToolCli.Commands.Options;
using PublishToolCli.Output;

namespace PublishToolCli.Commands;

[Command("version", Description = "Print the version of PublishToolCli")]
public class VersionCommand(VersionCommandOptions options) : ICommand<VersionCommandOptions>
{
    public VersionCommandOptions Options { get; } = options;

    public Task<int> ExecuteAsync(CancellationToken ct)
    {
        System.Console.WriteLine(Options.Json
            ? JsonSerializer.Serialize(new VersionResult { Status = ExitCodes.Status(ExitCodes.Success) }, CliJsonContext.Default.VersionResult)
            : $"PublishToolCli version {BuildInfo.Version}");
        return Task.FromResult(ExitCodes.Success);
    }
}
