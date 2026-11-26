using System.Diagnostics;
using PublishTool.Configuration;
using PublishTool.Deployment;
using PublishTool.Tests.Support;

namespace PublishTool.Tests.Deployment;

public class CleanTests
{
    private const string PreviousPackage = "uat_2026-01-01_12-00-00";
    private const string PackageName = "uat_2026-01-02_12-00-00";

    private sealed class Scenario
    {
        public required TempDirectory Temp { get; init; }
        public required DeployPackage Package { get; init; }
        public required ConfigStore<ServerConfig> Store { get; init; }
        public required ServerConfig Config { get; init; }
        public required string Target { get; init; }

        public DeployPlan Plan(IReadOnlyDictionary<string, TargetOverride>? overrides = null)
        {
            var resolution = DeployTargets.Resolve(Config, "uat", [.. Package.Manifest.Projects.Select(p => p.Name)], overrides);
            var plan = DeployPlan.Create(Package, resolution.Targets, Config, new FakeHostController(), BuildInfo.Version, CancellationToken.None);
            plan.Clean(Package.LoadInventory(), overrides, null, CancellationToken.None);
            return plan;
        }
    }

    private static Dictionary<string, string> Files(params (string Path, string Content)[] files) =>
        files.ToDictionary(f => f.Path, f => f.Content);

    private static Scenario Create(TempDirectory temp, bool withApi = false, string[]? keep = null)
    {
        temp.Write("target/App.dll", "old app");
        temp.Write("target/Lib.dll", "lib");
        temp.Write("target/Changed.dll", "edited on the server");
        temp.Write("target/Old.dll", "old file");
        temp.Write("target/Stray.dll", "stray");
        temp.Write("target/old/sub/x.txt", "stray");
        temp.Write("target/logs/app.log", "log");
        temp.Write("target/web.config", "server config");
        Directory.CreateDirectory(temp.Combine("target", "empty"));
        string target = temp.Combine("target");
        var store = new ConfigStore<ServerConfig>(
            temp.Combine("server.json"),
            ServerConfigVersions.Current,
            ServerConfigVersions.Parsers,
            ServerConfigVersions.Migrators,
            JsonContext.Default.ServerConfig);
        var config = store.Load();
        var environment = config.EnvironmentFor("uat");
        environment.Targets["Web"] = new DeployTarget { Path = target, Keep = [.. keep ?? []] };
        config.SetRecord(target, new DeployRecord { Package = PreviousPackage });
        var builder = new PackageBuilder(temp, PackageName)
            .Project("Web", PreviousPackage, false, Files(("App.dll", "new app")), Files(("Old.dll", "old file")), Files(("App.dll", "old app")))
            .Inventory("Web", Files(("App.dll", "new app"), ("Lib.dll", "lib"), ("Changed.dll", "original"), ("bin/Gone.dll", "gone")));
        if (withApi)
        {
            temp.Write("target/api/Api.dll", "api");
            temp.Write("target/api/Leftover.dll", "leftover");
            environment.Targets["Api"] = new DeployTarget { Path = temp.Combine("target", "api"), ServiceName = "ApiService" };
            config.SetRecord(temp.Combine("target", "api"), new DeployRecord { Package = PreviousPackage });
            builder.Inventory("Api", Files(("Api.dll", "api")));
        }

        store.Save(config);
        return new Scenario { Temp = temp, Package = builder.Build(), Store = store, Config = config, Target = target };
    }

    private static DeployWarning Warning(DeployPlan plan, string code, string project) =>
        Assert.Single(plan.Warnings, w => w.Code == code && w.Message.StartsWith($"{project}:", StringComparison.Ordinal));

    [Fact]
    public void Clean_SortsTargetFilesIntoStrayMismatchedMissingAndProtected()
    {
        using var temp = new TempDirectory();
        var plan = Create(temp, keep: ["logs/**"]).Plan();

        var web = Assert.Single(plan.Targets);
        Assert.Equal(["Stray.dll", "old/sub/x.txt"], web.Stray.Order(StringComparer.Ordinal));
        Assert.Equal(["empty", "old", "old/sub"], web.EmptyDirectories.Order(StringComparer.Ordinal));
        Assert.True(web.EmptyDirectories.IndexOf("old/sub") < web.EmptyDirectories.IndexOf("old"));
        Assert.Equal(["Web/web.config"], plan.ProtectedConfigFiles);
        Assert.Equal(
            ["Stray.dll", "empty/", "old/", "old/sub/", "old/sub/x.txt"],
            Assert.IsAssignableFrom<IReadOnlyList<string>>(Warning(plan, WarningCodes.CleanDelete, "Web").Files).Order(StringComparer.Ordinal));
        Assert.Equal(["Changed.dll"], Warning(plan, WarningCodes.CleanMismatch, "Web").Files);
        Assert.Equal(["bin/Gone.dll"], Warning(plan, WarningCodes.CleanMissing, "Web").Files);
        Assert.True(plan.IsCleaned);
        Assert.Empty(plan.Errors);
    }

    [Fact]
    public void Clean_WithoutKeepPattern_DeletesTheLogs()
    {
        using var temp = new TempDirectory();
        var plan = Create(temp).Plan();

        var web = Assert.Single(plan.Targets);
        Assert.Contains("logs/app.log", web.Stray);
        Assert.Contains("logs", web.EmptyDirectories);
    }

    [Fact]
    public void Clean_KeepPattern_SuppressesMismatchedAndMissingFilesToo()
    {
        using var temp = new TempDirectory();
        var plan = Create(temp, keep: ["logs", "Changed.dll", "bin", "*.txt"]).Plan();

        var web = Assert.Single(plan.Targets);
        Assert.Equal(["Stray.dll"], web.Stray);
        Assert.Equal(["empty"], web.EmptyDirectories);
        Assert.DoesNotContain(plan.Warnings, w => w.Code is WarningCodes.CleanMismatch or WarningCodes.CleanMissing);
    }

    [Fact]
    public void Clean_RunOverride_AddsKeepPatternsWithoutChangingTheConfig()
    {
        using var temp = new TempDirectory();
        var scenario = Create(temp);
        var overrides = new Dictionary<string, TargetOverride>(StringComparer.OrdinalIgnoreCase) { ["web"] = new() };
        overrides["web"].Keep.Add("logs/**");

        var plan = scenario.Plan(overrides);

        Assert.DoesNotContain("logs/app.log", Assert.Single(plan.Targets).Stray);
        Assert.Empty(scenario.Config.Environments["uat"].Targets["Web"].Keep);
    }

    [Fact]
    public void Clean_NestedTarget_IsProtectedAndCleanedOnItsOwn()
    {
        using var temp = new TempDirectory();
        var plan = Create(temp, withApi: true, keep: ["logs/**"]).Plan();

        var web = plan.Targets[0];
        var api = plan.Targets[1];
        Assert.DoesNotContain(web.Stray, f => f.StartsWith("api/", StringComparison.Ordinal));
        Assert.DoesNotContain("api", web.EmptyDirectories);
        Assert.True(api.IsCleanOnly);
        Assert.Equal("ApiService", api.ServiceName);
        Assert.Equal(["Leftover.dll"], api.Stray);
        Assert.False(plan.IsEmpty);
    }

    [Fact]
    public void Clean_ProjectWithoutChangesAndNothingStray_AddsNoTarget()
    {
        using var temp = new TempDirectory();
        var scenario = Create(temp, withApi: true);
        File.Delete(temp.Combine("target", "api", "Leftover.dll"));

        var plan = scenario.Plan();

        Assert.Equal("Web", Assert.Single(plan.Targets).Project.Name);
    }

    [Fact]
    public void Clean_ProjectWithoutTarget_IsReportedAndNotCleaned()
    {
        using var temp = new TempDirectory();
        var scenario = Create(temp, withApi: true);
        scenario.Config.Environments["uat"].Targets.Remove("Api");

        var plan = scenario.Plan();

        Assert.Equal(["Api"], plan.NotCleaned);
        Assert.Equal("Web", Assert.Single(plan.Targets).Project.Name);
    }

    [Fact]
    public void Clean_HiddenFiles_AreStrayAndLinkedDirectoriesAreLeftAlone()
    {
        using var temp = new TempDirectory();
        var scenario = Create(temp, keep: ["logs/**"]);
        string hidden = temp.Write("target/Hidden.dll", "hidden");
        File.SetAttributes(hidden, File.GetAttributes(hidden) | FileAttributes.Hidden);
        temp.Write("outside/Important.txt", "important");
        bool linked = TryCreateJunction(temp.Combine("target", "link"), temp.Combine("outside"));

        var plan = scenario.Plan();

        var web = Assert.Single(plan.Targets);
        Assert.Contains("Hidden.dll", web.Stray);
        Assert.DoesNotContain(web.Stray, f => f.StartsWith("link/", StringComparison.Ordinal));
        Assert.DoesNotContain("link", web.EmptyDirectories);
        Assert.True(!linked || Directory.Exists(temp.Combine("target", "link")));
    }

    [Fact]
    public void ResetClean_RemovesEverythingTheCleanAdded()
    {
        using var temp = new TempDirectory();
        var plan = Create(temp, withApi: true).Plan();

        plan.ResetClean();

        var web = Assert.Single(plan.Targets);
        Assert.Empty(web.Stray);
        Assert.Empty(web.EmptyDirectories);
        Assert.Empty(plan.Warnings);
        Assert.Empty(plan.ProtectedConfigFiles);
        Assert.False(plan.IsCleaned);
    }

    [Fact]
    public void LoadInventory_MissingOrOfAnotherPackage_Throws()
    {
        using var temp = new TempDirectory();
        var without = new PackageBuilder(temp, PackageName).Project("Web", null, true, Files(("App.dll", "app"))).Build();
        var foreign = new PackageBuilder(temp, "uat_2026-01-03_12-00-00") { InventoryPackage = PackageName }
            .Project("Web", null, true, Files(("App.dll", "app")))
            .Inventory("Web", Files(("App.dll", "app")))
            .Build();

        Assert.False(without.HasInventory);
        Assert.Throws<DeployException>(() => without.LoadInventory());
        Assert.Throws<DeployException>(() => foreign.LoadInventory());
    }

    [Fact]
    public async Task Deploy_WithClean_DeletesStrayFilesAndRollbackRestoresThem()
    {
        using var temp = new TempDirectory();
        var scenario = Create(temp, withApi: true, keep: ["logs/**"]);
        File.SetAttributes(temp.Combine("target", "Stray.dll"), FileAttributes.ReadOnly);
        var plan = scenario.Plan();
        var hosts = new FakeHostController().WithService("ApiService");

        var outcome = await new DeployRunner(plan, scenario.Config, scenario.Store, hosts, BuildInfo.Version, "PublishToolCli", new RecordingSteps())
            .RunAsync(CancellationToken.None);

        Assert.Equal(DeployOutcome.Deployed, outcome);
        Assert.Equal("new app", temp.Read("target/App.dll"));
        Assert.False(temp.Exists("target/Stray.dll"));
        Assert.False(temp.Exists("target/Old.dll"));
        Assert.False(Directory.Exists(temp.Combine("target", "old")));
        Assert.False(Directory.Exists(temp.Combine("target", "empty")));
        Assert.False(temp.Exists("target/api/Leftover.dll"));
        Assert.Equal("log", temp.Read("target/logs/app.log"));
        Assert.Equal("server config", temp.Read("target/web.config"));
        Assert.Equal("edited on the server", temp.Read("target/Changed.dll"));
        Assert.Equal("api", temp.Read("target/api/Api.dll"));
        var stored = scenario.Store.Load();
        Assert.Equal(PackageName, stored.RecordFor(scenario.Target)?.Package);
        Assert.Equal(PreviousPackage, stored.RecordFor(temp.Combine("target", "api"))?.Package);
        Assert.Contains("stop ApiService", hosts.Calls);

        var journal = PackageJson.ReadJournal(plan.Package.JournalPath);
        Assert.Empty(RollbackRunner.CheckChain(journal, stored));
        bool rolledBack = await new RollbackRunner(plan.Package.Directory, journal, stored, scenario.Store, new FakeHostController().WithService("ApiService"),
            new RecordingSteps()).RunAsync();

        Assert.True(rolledBack);
        Assert.Equal("old app", temp.Read("target/App.dll"));
        Assert.Equal("stray", temp.Read("target/Stray.dll"));
        Assert.Equal("stray", temp.Read("target/old/sub/x.txt"));
        Assert.Equal("leftover", temp.Read("target/api/Leftover.dll"));
        Assert.True(Directory.Exists(temp.Combine("target", "empty")));
        var restored = scenario.Store.Load();
        Assert.Equal(PreviousPackage, restored.RecordFor(scenario.Target)?.Package);
        Assert.Equal(PreviousPackage, restored.RecordFor(temp.Combine("target", "api"))?.Package);
        Assert.False(Directory.Exists(plan.Package.BackupDirectory));
    }

    private static bool TryCreateJunction(string link, string target)
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        try
        {
            using var process = Process.Start(new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            });
            process?.WaitForExit();
            return process?.ExitCode == 0;
        }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }
}
