using System.Text.Json.Serialization;
using PublishTool.Configuration;

namespace PublishTool.Deployment;

public enum JournalState
{
    BackingUp,
    Applying,
    RestoreFailed,
    Deployed
}

public sealed class DeployJournal
{
    public const int CurrentFormat = 1;
    public const string FileName = "journal.json";

    public int Format { get; set; } = CurrentFormat;

    public string Package { get; set; } = string.Empty;

    public string ToolVersion { get; set; } = string.Empty;

    [JsonConverter(typeof(JsonStringEnumConverter<JournalState>))]
    public JournalState State { get; set; }

    public DateTimeOffset Started { get; set; }

    public bool RestartIis { get; set; }

    public List<string> RestartServices { get; set; } = [];

    public List<JournalTarget> Targets { get; set; } = [];
}

public sealed class JournalTarget
{
    public string Project { get; set; } = string.Empty;

    public string Path { get; set; } = string.Empty;

    public string? ServiceName { get; set; }

    public DeployRecord? Previous { get; set; }

    public List<string> BackedUp { get; set; } = [];

    public List<string> Created { get; set; } = [];

    public List<string> CreatedDirectories { get; set; } = [];

    public bool CleanOnly { get; set; }

    public List<string> RemovedDirectories { get; set; } = [];
}
