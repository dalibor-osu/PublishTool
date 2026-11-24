using PublishTool.Configuration;
using PublishTool.Deployment;
using PublishTool.Progress;
using PublishTool.Tests.Support;

namespace PublishTool.Tests.Deployment;

public class RunnerTests
{
    private const string PreviousPackage = "uat_2026-01-01_12-00-00";
    private const string PackageName = "uat_2026-01-02_12-00-00";

    private sealed class Scenario
    {
        public required TempDirectory Temp { get; init; }
        public required DeployPlan Plan { get; init; }
        public required ConfigStore<ServerConfig> Store { get; init; }
        public required ServerConfig Config { get; init; }
        public required string Target { get; init; }
    }

    private static Scenario Create(TempDirectory temp)
    {
        temp.Write("target/App.dll", "old app");
        temp.Write("target/Old.dll", "old file");
        string target = temp.Combine("target");
        var store = new ConfigStore<ServerConfig>(
            temp.Combine("server.json"),
            ServerConfigVersions.Current,
            ServerConfigVersions.Parsers,
            ServerConfigVersions.Migrators,
            JsonContext.Default.ServerConfig);
        var config = store.Load();
        config.SetRecord(target, new DeployRecord { Package = PreviousPackage });
        store.Save(config);
        var package = new PackageBuilder(temp, PackageName)
            .Project("Web", PreviousPackage, false, new Dictionary<string, string> { ["App.dll"] = "new app", ["bin/New.dll"] = "new" },
                new Dictionary<string, string> { ["Old.dll"] = "old file" }, new Dictionary<string, string> { ["App.dll"] = "old app" })
            .Build();
        var plan = DeployPlan.Create(package, new Dictionary<string, DeployTarget> { ["Web"] = new() { Path = target } }, config,
            new FakeHostController(), BuildInfo.Version, CancellationToken.None);
        return new Scenario { Temp = temp, Plan = plan, Store = store, Config = config, Target = target };
    }

    private static Task<DeployOutcome> Deploy(Scenario scenario, FakeHostController hosts, RecordingSteps? steps = null) =>
        new DeployRunner(scenario.Plan, scenario.Config, scenario.Store, hosts, BuildInfo.Version, "PublishToolCli", steps ?? new RecordingSteps())
            .RunAsync(CancellationToken.None);

    [Fact]
    public async Task Deploy_Success_AppliesRecordsAndRestartsIis()
    {
        using var temp = new TempDirectory();
        var scenario = Create(temp);
        var hosts = new FakeHostController();
        var steps = new RecordingSteps();

        var outcome = await Deploy(scenario, hosts, steps);

        Assert.Equal(DeployOutcome.Deployed, outcome);
        Assert.Equal("new app", temp.Read("target/App.dll"));
        Assert.False(temp.Exists("target/Old.dll"));
        Assert.Equal(["stop IIS", "start IIS"], hosts.Calls);
        Assert.Equal(PackageName, scenario.Store.Load().RecordFor(scenario.Target)?.Package);
        Assert.Equal(JournalState.Deployed, PackageJson.ReadJournal(scenario.Plan.Package.JournalPath).State);
        Assert.All(steps.Steps, s => Assert.True(s.IsFinished));
    }

    [Fact]
    public async Task Deploy_StartFails_RestoresTheBackupAndKeepsTheRecord()
    {
        using var temp = new TempDirectory();
        var scenario = Create(temp);
        var hosts = new FakeHostController { FailIisStart = true };

        var outcome = await Deploy(scenario, hosts);

        Assert.Equal(DeployOutcome.Failed, outcome);
        Assert.Equal("old app", temp.Read("target/App.dll"));
        Assert.Equal("old file", temp.Read("target/Old.dll"));
        Assert.False(temp.Exists("target/bin/New.dll"));
        Assert.False(Directory.Exists(scenario.Plan.Package.BackupDirectory));
        Assert.Equal(PreviousPackage, scenario.Store.Load().RecordFor(scenario.Target)?.Package);
    }

    [Fact]
    public async Task Deploy_RestoreFails_ReportsRestoreFailedAndKeepsTheBackup()
    {
        using var temp = new TempDirectory();
        var scenario = Create(temp);
        var hosts = new FakeHostController { FailIisStart = true };
        string backedUpFile = Path.Combine(scenario.Plan.Package.BackupDirectory, "Web", "App.dll");
        hosts.OnIisStart = () => File.Delete(backedUpFile);
        var steps = new RecordingSteps();

        var outcome = await Deploy(scenario, hosts, steps);

        Assert.Equal(DeployOutcome.RestoreFailed, outcome);
        Assert.Equal(JournalState.RestoreFailed, PackageJson.ReadJournal(scenario.Plan.Package.JournalPath).State);
        Assert.Contains(steps.Steps, s => s.State == StepState.Failed && s.Detail?.Contains("PublishToolCli rollback") == true);
    }

    [Fact]
    public async Task Rollback_AfterDeploy_RestoresFilesAndRecords()
    {
        using var temp = new TempDirectory();
        var scenario = Create(temp);
        await Deploy(scenario, new FakeHostController());
        var journal = PackageJson.ReadJournal(scenario.Plan.Package.JournalPath);
        var config = scenario.Store.Load();

        Assert.Empty(RollbackRunner.CheckChain(journal, config));
        bool rolledBack = await new RollbackRunner(scenario.Plan.Package.Directory, journal, config, scenario.Store, new FakeHostController(),
            new RecordingSteps()).RunAsync();

        Assert.True(rolledBack);
        Assert.Equal("old app", temp.Read("target/App.dll"));
        Assert.Equal("old file", temp.Read("target/Old.dll"));
        Assert.Equal(PreviousPackage, scenario.Store.Load().RecordFor(scenario.Target)?.Package);
        Assert.False(Directory.Exists(scenario.Plan.Package.BackupDirectory));
    }

    [Fact]
    public async Task CheckChain_NewerDeployOnTheTarget_WarnsWithCode()
    {
        using var temp = new TempDirectory();
        var scenario = Create(temp);
        await Deploy(scenario, new FakeHostController());
        var config = scenario.Store.Load();
        config.SetRecord(scenario.Target, new DeployRecord { Package = "uat_2026-01-03_12-00-00" });

        var warnings = RollbackRunner.CheckChain(PackageJson.ReadJournal(scenario.Plan.Package.JournalPath), config);

        Assert.Equal(WarningCodes.NewerDeploy, Assert.Single(warnings).Code);
    }
}
