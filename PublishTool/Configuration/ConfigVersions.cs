namespace PublishTool.Configuration;

/// <summary>
/// Registry of config file format versions.
///
/// Bump <see cref="Current"/> ONLY for a structural change the previous model cannot read anymore (renamed or re-shaped
/// properties, a dictionary that becomes a list, ...). Adding a new setting is not a new version: it deserializes as its default
/// and <see cref="ConfigHandler"/> prompts for it. Downgrading is not supported; a file newer than <see cref="Current"/> is rejected.
///
/// Recipe for a new version N (from N-1):
///  1. Copy the current <c>Config</c> class to <c>Versions/ConfigV{N-1}.cs</c> as a frozen snapshot (drop <c>IVersionedConfig</c>,
///     keep the <c>Version</c> property as a plain int so the snapshot still reads its own files).
///  2. Change <c>Config</c> to the new shape and set <see cref="Current"/> to N.
///  3. Register the snapshot in <see cref="JsonContext"/> and add <c>new ConfigParser&lt;ConfigV{N-1}&gt;(N-1, JsonContext.Default.ConfigV{N-1})</c>
///     to <see cref="Parsers"/>; change the entry for the current version to <c>ConfigParser&lt;Config&gt;(N, ...)</c>.
///  4. Add <c>Migrations/ConfigV{N-1}ToV{N}.cs</c> deriving from <c>ConfigMigrator&lt;ConfigV{N-1}, Config&gt;</c> (or to the next
///     snapshot if it is not the newest step) and add it to <see cref="Migrators"/>.
/// <see cref="ConfigStore{TConfig}"/> validates at startup that every version 1..Current has a parser and every step has a migrator.
/// </summary>
public static class ConfigVersions
{
    public const int Current = 1;

    public static readonly IReadOnlyList<IConfigParser> Parsers =
    [
        new ConfigParser<Config>(1, JsonContext.Default.Config)
    ];

    public static readonly IReadOnlyList<IConfigMigrator> Migrators = [];
}
