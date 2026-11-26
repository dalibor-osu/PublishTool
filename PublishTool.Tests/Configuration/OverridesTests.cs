using PublishTool.Configuration;
using PublishTool.Deployment;

namespace PublishTool.Tests.Configuration;

public class OverridesTests
{
    private static ServerConfig StoredTargets()
    {
        var config = new ServerConfig();
        var environment = config.EnvironmentFor("uat");
        environment.Targets["Web"] = new DeployTarget { Path = @"C:\sites\web" };
        environment.Targets["Worker"] = new DeployTarget { Path = @"C:\apps\worker", ServiceName = "WorkerService" };
        return config;
    }

    [Fact]
    public void Resolve_Overrides_WinOverStoredTargetsWithoutChangingTheConfig()
    {
        var config = StoredTargets();
        var overrides = new Dictionary<string, TargetOverride>
        {
            ["web"] = new() { Path = @"D:\other\web" },
            ["Worker"] = new() { ServiceSet = true, ServiceName = null },
            ["Api"] = new() { Path = @"D:\api", ServiceName = "ApiService", ServiceSet = true }
        };

        var resolution = DeployTargets.Resolve(config, "UAT", ["Web", "Worker", "Api"], overrides);

        Assert.Empty(resolution.Missing);
        Assert.Equal(@"D:\other\web", resolution.Targets["Web"].Path);
        Assert.Null(resolution.Targets["Worker"].ServiceName);
        Assert.Equal("ApiService", resolution.Targets["Api"].ServiceName);
        Assert.Equal(@"C:\sites\web", config.Environments["uat"].Targets["Web"].Path);
        Assert.Equal("WorkerService", config.Environments["uat"].Targets["Worker"].ServiceName);
        Assert.False(config.Environments["uat"].Targets.ContainsKey("Api"));
    }

    [Fact]
    public void Resolve_KeepPatterns_AreCopiedAndMergedWithTheOverride()
    {
        var config = StoredTargets();
        config.Environments["uat"].Targets["Web"].Keep.Add("logs/**");
        var overrides = new Dictionary<string, TargetOverride> { ["web"] = new() { Path = @"D:\other\web" } };
        overrides["web"].Keep.AddRange(["LOGS/**", "*.log"]);

        var resolution = DeployTargets.Resolve(config, "uat", ["Web"], overrides);

        Assert.Equal(["logs/**", "*.log"], resolution.Targets["Web"].Keep);
        Assert.Equal(["logs/**"], config.Environments["uat"].Targets["Web"].Keep);
    }

    [Fact]
    public void Merge_ChangingATarget_KeepsItsKeepPatterns()
    {
        var stored = new DeployTarget { Path = @"C:\sites\web", Keep = ["logs/**"] };
        var change = new TargetOverride { Path = @"D:\sites\web" };
        change.Keep.Add("*.log");

        var merged = PublishToolCli.Commands.ConfigCommand.Merge(stored, "Web", change);

        Assert.Equal(@"D:\sites\web", merged.Path);
        Assert.Equal(["logs/**", "*.log"], merged.Keep);
    }

    [Fact]
    public void ServerConfig_KeepPatterns_SurviveSavingAndLoading()
    {
        using var temp = new Support.TempDirectory();
        var store = new ConfigStore<ServerConfig>(
            temp.Combine("server.json"),
            ServerConfigVersions.Current,
            ServerConfigVersions.Parsers,
            ServerConfigVersions.Migrators,
            JsonContext.Default.ServerConfig);
        var config = store.Load();
        var target = new DeployTarget { Path = @"C:\sites\web" };
        Assert.True(target.AddKeep("logs/**"));
        Assert.False(target.AddKeep("LOGS/**"));
        config.EnvironmentFor("uat").Targets["Web"] = target;
        store.Save(config);

        var loaded = store.Load().Environments["uat"].Targets["Web"];

        Assert.Equal(["logs/**"], loaded.Keep);
        Assert.True(loaded.RemoveKeep("Logs/**"));
        Assert.Empty(loaded.Keep);
    }

    [Fact]
    public void Resolve_UnknownPrefix_ReportsMissingAndAddsNoEnvironment()
    {
        var config = new ServerConfig();

        var resolution = DeployTargets.Resolve(config, "prod", ["Web"]);

        Assert.Equal(["Web"], resolution.Missing);
        Assert.Empty(config.Environments);
    }

    [Theory]
    [InlineData(@"C:\sites\web\", true, @"C:\sites\web")]
    [InlineData("relative\\path", false, "relative\\path")]
    public void TryNormalizePath_AcceptsOnlyAbsolutePaths(string value, bool expected, string normalized)
    {
        Assert.Equal(expected, DeployTargets.TryNormalizePath(value, out string result));
        Assert.Equal(normalized, result);
    }

    [Fact]
    public void With_OverridesReplaceStoredValuesInACopy()
    {
        var stored = new DirectorySettings { PublishDirectory = @"C:\publish", SharedDirectory = @"\\server\share", PublishableProjects = ["Web"] };

        var merged = stored.With(new SettingsOverrides { PublishDirectory = @"D:\publish", PublishableProjects = ["Api"] });

        Assert.Equal(@"D:\publish", merged.PublishDirectory);
        Assert.Equal(@"\\server\share", merged.SharedDirectory);
        Assert.Equal(["Api"], Assert.IsType<string[]>(merged.PublishableProjects));
        Assert.Equal(@"C:\publish", stored.PublishDirectory);
    }

    [Fact]
    public void MissingFor_DependsOnLocalAndExplicitProjects()
    {
        var empty = new DirectorySettings();

        Assert.Equal(
            [SettingKeys.PublishDirectory, SettingKeys.SharedDirectory, SettingKeys.DeployDirectory, SettingKeys.PublishableProjects],
            empty.MissingFor(local: false, projectsGiven: false));
        Assert.Equal([SettingKeys.PublishDirectory], empty.MissingFor(local: true, projectsGiven: true));
    }

    [Fact]
    public void NotAccepted_FiltersByCodeIgnoringCase()
    {
        DeployWarning[] warnings =
        [
            new(WarningCodes.ToolVersion, "version"),
            new(WarningCodes.ChainMismatch, "chain")
        ];

        var left = WarningCodes.NotAccepted(warnings, ["TOOL-VERSION"]);

        Assert.Equal(WarningCodes.ChainMismatch, Assert.Single(left).Code);
    }
}
