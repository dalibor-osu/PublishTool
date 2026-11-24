namespace PublishToolCli;

public static class ExitCodes
{
    public const int Success = 0;
    public const int Failed = 1;
    public const int InvalidArguments = 2;
    public const int WarningsNotAccepted = 3;
    public const int RestoreFailed = 4;
    public const int Cancelled = 130;

    public static string Status(int code) => code switch
    {
        Success => "succeeded",
        InvalidArguments => "invalid-arguments",
        WarningsNotAccepted => "warnings-not-accepted",
        RestoreFailed => "restore-failed",
        Cancelled => "cancelled",
        _ => "failed"
    };

    public const string Help = """
        Exit codes:
          0    success; a dry run exits 0 when the package and the plan are valid, whatever warnings it lists
          1    the operation failed
          2    invalid arguments or missing configuration
          3    stopped on warnings that were not accepted with --accept
          4    a deploy failed and its automatic restore failed too; run 'PublishToolCli rollback --yes -dir <package>'
          130  cancelled with Ctrl+C
        """;
}

public sealed class CliArgumentException(string message) : Exception(message);
