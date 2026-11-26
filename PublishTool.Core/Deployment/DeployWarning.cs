namespace PublishTool.Deployment;

public sealed record DeployWarning(string Code, string Message, IReadOnlyList<string>? Files = null)
{
    public const int MaxListedFiles = 10;

    public static string ListFiles(IReadOnlyList<string> files) =>
        files.Count <= MaxListedFiles
            ? string.Join(", ", files)
            : $"{string.Join(", ", files.Take(MaxListedFiles))} and {files.Count - MaxListedFiles} more";
}

public static class WarningCodes
{
    public const string ToolVersion = "tool-version";
    public const string TargetMissing = "target-missing";
    public const string AlreadyDeployed = "already-deployed";
    public const string FullPublish = "full-publish";
    public const string UnknownBase = "unknown-base";
    public const string NoRecord = "no-record";
    public const string ChainMismatch = "chain-mismatch";
    public const string ConfigChanged = "config-changed";
    public const string TargetModified = "target-modified";
    public const string NewerDeploy = "newer-deploy";
    public const string Downgrade = "downgrade";
    public const string CleanDelete = "clean-delete";
    public const string CleanMismatch = "clean-mismatch";
    public const string CleanMissing = "clean-missing";

    public static readonly IReadOnlyList<string> Clean = [CleanDelete, CleanMismatch, CleanMissing];

    public static readonly IReadOnlyList<string> All =
    [
        ToolVersion,
        TargetMissing,
        AlreadyDeployed,
        FullPublish,
        UnknownBase,
        NoRecord,
        ChainMismatch,
        ConfigChanged,
        TargetModified,
        NewerDeploy,
        Downgrade,
        CleanDelete,
        CleanMismatch,
        CleanMissing
    ];

    public static bool IsClean(string code) => Clean.Contains(code, StringComparer.OrdinalIgnoreCase);

    public static bool IsKnown(string code) => All.Contains(code, StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<DeployWarning> NotAccepted(IEnumerable<DeployWarning> warnings, IReadOnlyCollection<string> accepted) =>
        [.. warnings.Where(w => !accepted.Contains(w.Code, StringComparer.OrdinalIgnoreCase))];
}
