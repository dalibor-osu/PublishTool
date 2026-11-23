namespace PublishTool.Configuration;

public static class ServerConfigVersions
{
    public const int Current = 1;

    public static readonly IReadOnlyList<IConfigParser> Parsers =
    [
        new ConfigParser<ServerConfig>(1, JsonContext.Default.ServerConfig)
    ];

    public static readonly IReadOnlyList<IConfigMigrator> Migrators = [];
}
