using PublishTool.Configuration.Migrations;
using PublishTool.Configuration.Versions;

namespace PublishTool.Configuration;

public static class ConfigVersions
{
    public const int Current = 2;

    public static readonly IReadOnlyList<IConfigParser> Parsers =
    [
        new ConfigParser<ConfigV1>(1, JsonContext.Default.ConfigV1),
        new ConfigParser<Config>(2, JsonContext.Default.Config)
    ];

    public static readonly IReadOnlyList<IConfigMigrator> Migrators =
    [
        new ConfigV1ToV2()
    ];
}
