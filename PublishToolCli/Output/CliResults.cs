using System.Text.Json.Serialization;
using PublishTool;
using PublishTool.Configuration;

namespace PublishToolCli.Output;

public class CliResult(string command)
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public string Command { get; set; } = command;

    public string Status { get; set; } = string.Empty;

    public int ExitCode { get; set; }

    public List<CliWarning> Warnings { get; set; } = [];

    public List<string> Errors { get; set; } = [];

    public List<CliStep> Steps { get; set; } = [];
}

public sealed record CliWarning(string Code, string Message, bool Accepted, IReadOnlyList<string>? Files = null);

public sealed record CliStep(string Name, string State, string? Detail);

public sealed class VersionResult() : CliResult("version")
{
    public string Version { get; set; } = BuildInfo.Version;
}

public sealed class PublishCliResult() : CliResult("publish")
{
    public string WorkingDirectory { get; set; } = string.Empty;

    public string? Configuration { get; set; }

    public bool Local { get; set; }

    public List<string> Projects { get; set; } = [];

    public string? Prefix { get; set; }

    public string? PackageName { get; set; }

    public string? PackageDirectory { get; set; }

    public string? Reference { get; set; }

    public string? PreviousReference { get; set; }

    public List<PublishedProject> Changes { get; set; } = [];

    public List<string> Carried { get; set; } = [];

    public List<string> Pruned { get; set; } = [];
}

public sealed class DeployCliResult() : CliResult("deploy")
{
    public string PackageDirectory { get; set; } = string.Empty;

    public string? Package { get; set; }

    public string? Prefix { get; set; }

    public bool DryRun { get; set; }

    public bool Changed { get; set; }

    public List<string> MissingTargets { get; set; } = [];

    public List<CliDeployTarget> Targets { get; set; } = [];

    public List<string> ExtraFiles { get; set; } = [];
}

public sealed record CliDeployTarget(
    string Project,
    string Path,
    string? ServiceName,
    int Create,
    int Overwrite,
    int Delete,
    string? DeployedNow,
    string? BasedOn,
    bool IsFullPublish);

public sealed class RollbackCliResult() : CliResult("rollback")
{
    public string PackageDirectory { get; set; } = string.Empty;

    public string? Package { get; set; }

    public string? JournalState { get; set; }

    public bool DryRun { get; set; }

    public bool Changed { get; set; }

    public List<CliRollbackTarget> Targets { get; set; } = [];
}

public sealed record CliRollbackTarget(string Project, string Path, string? ServiceName, int Restore, int Remove, string? RecordAfter);

public sealed class ConfigCliResult() : CliResult("config")
{
    public string ConfigPath { get; set; } = string.Empty;

    public string? Directory { get; set; }

    public DirectorySettings? Settings { get; set; }

    public List<string> MissingSettings { get; set; } = [];

    public string? Prefix { get; set; }

    public Dictionary<string, DeployTarget>? Targets { get; set; }

    public bool Changed { get; set; }
}

public sealed class UpdateCliResult() : CliResult("update")
{
    public string CurrentVersion { get; set; } = BuildInfo.Version;

    public string? TargetVersion { get; set; }

    public bool FetchOnly { get; set; }

    public bool Updated { get; set; }
}

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(CliResult))]
[JsonSerializable(typeof(VersionResult))]
[JsonSerializable(typeof(PublishCliResult))]
[JsonSerializable(typeof(DeployCliResult))]
[JsonSerializable(typeof(RollbackCliResult))]
[JsonSerializable(typeof(ConfigCliResult))]
[JsonSerializable(typeof(UpdateCliResult))]
public partial class CliJsonContext : JsonSerializerContext;
