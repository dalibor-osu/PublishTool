using PublishTool.Configuration;
using PublishTool.Tests.Support;

namespace PublishTool.Tests.Configuration;

public class ConfigStoreTests
{
    private static ConfigStore<Config> CreateStore(TempDirectory temp) => new(
        temp.Combine("config.json"),
        ConfigVersions.Current,
        ConfigVersions.Parsers,
        ConfigVersions.Migrators,
        JsonContext.Default.Config);

    [Fact]
    public void Load_MissingFile_ReturnsEmptyConfigOfCurrentVersion()
    {
        using var temp = new TempDirectory();

        var config = CreateStore(temp).Load();

        Assert.Equal(ConfigVersions.Current, config.Version);
        Assert.Empty(config.Directories);
    }

    [Fact]
    public void Load_Version1File_MigratesPerDirectoryAndKeepsBackup()
    {
        using var temp = new TempDirectory();
        temp.Write("config.json", """
            {
              "PublishDirectories": { "C:\\repo": "C:\\publish" },
              "SharedDirectories": { "C:\\repo": "\\\\server\\share" },
              "DeployDirectories": { "C:\\repo": "C:\\deploy" },
              "IgnoredDirectories": { "C:\\repo": [".git"] },
              "PublishableProjects": { "C:\\repo": ["Web"], "C:\\other": ["Api"] }
            }
            """);
        var store = CreateStore(temp);

        var config = store.Load();

        var repo = config.Directories[@"C:\repo"];
        Assert.True(repo.IsComplete);
        Assert.Equal(@"C:\publish", repo.PublishDirectory);
        Assert.Equal(["Web"], repo.PublishableProjects);
        var other = config.Directories[@"C:\other"];
        Assert.False(other.IsComplete);
        Assert.Null(other.PublishDirectory);
        Assert.True(File.Exists(store.BackupPath(1)));
        Assert.Equal(ConfigVersions.Current, ConfigStore<Config>.ReadVersion(File.ReadAllText(store.Path)));
    }

    [Fact]
    public void Load_NewerVersion_ThrowsConfigException()
    {
        using var temp = new TempDirectory();
        temp.Write("config.json", $$"""{ "Version": {{ConfigVersions.Current + 1}} }""");

        Assert.Throws<ConfigException>(() => CreateStore(temp).Load());
    }

    [Fact]
    public void Save_ServerConfig_RoundTrips()
    {
        using var temp = new TempDirectory();
        var store = new ConfigStore<ServerConfig>(
            temp.Combine("server.json"),
            ServerConfigVersions.Current,
            ServerConfigVersions.Parsers,
            ServerConfigVersions.Migrators,
            JsonContext.Default.ServerConfig);
        var config = store.Load();
        config.EnvironmentFor("uat").Targets["Web"] = new DeployTarget { Path = @"C:\inetpub\web" };
        config.SetRecord(@"C:\inetpub\web", new DeployRecord { Package = "uat_2026-01-01_12-00-00" });

        store.Save(config);
        var loaded = store.Load();

        Assert.Equal(@"C:\inetpub\web", loaded.EnvironmentFor("UAT").TargetFor("web")?.Path);
        Assert.Equal("uat_2026-01-01_12-00-00", loaded.RecordFor(@"c:\INETPUB\web")?.Package);
    }
}
