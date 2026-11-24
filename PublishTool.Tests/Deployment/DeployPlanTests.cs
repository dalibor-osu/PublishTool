using PublishTool.Configuration;
using PublishTool.Deployment;
using PublishTool.Tests.Support;

namespace PublishTool.Tests.Deployment;

public class DeployPlanTests
{
    private const string PackageName = "uat_2026-01-02_12-00-00";
    private const string PreviousPackage = "uat_2026-01-01_12-00-00";

    private static Dictionary<string, DeployTarget> Map(params (string Project, string Path, string? Service)[] targets) =>
        targets.ToDictionary(t => t.Project, t => new DeployTarget { Path = t.Path, ServiceName = t.Service }, StringComparer.OrdinalIgnoreCase);

    private static Dictionary<string, string> Files(params (string Path, string Content)[] files) =>
        files.ToDictionary(f => f.Path, f => f.Content);

    private static DeployPackage WebPackage(TempDirectory temp, string? basePackage = PreviousPackage, bool isFullPublish = false) =>
        new PackageBuilder(temp, PackageName)
            .Project("Web", basePackage, isFullPublish, Files(("App.dll", "new"), ("bin/New.dll", "new")), Files(("Old.dll", "old")),
                isFullPublish ? null : Files(("App.dll", "old")))
            .Build();

    private static ServerConfig RecordedAt(string targetPath, string package)
    {
        var config = new ServerConfig();
        config.SetRecord(targetPath, new DeployRecord { Package = package });
        return config;
    }

    private static DeployPlan Plan(DeployPackage package, string target, ServerConfig? config = null) =>
        DeployPlan.Create(package, Map(("Web", target, null)), config ?? RecordedAt(target, PreviousPackage), new FakeHostController(),
            BuildInfo.Version, CancellationToken.None);

    [Fact]
    public void Create_SortsFilesIntoCreateOverwriteAndDelete()
    {
        using var temp = new TempDirectory();
        temp.Write("target/App.dll", "old");
        temp.Write("target/Old.dll", "old");
        string target = temp.Combine("target");

        var plan = Plan(WebPackage(temp), target);

        var web = Assert.Single(plan.Targets);
        Assert.Equal(["App.dll"], web.Overwrite);
        Assert.Equal(["bin/New.dll"], web.Create);
        Assert.Equal(["Old.dll"], web.Delete);
        Assert.Equal([temp.Combine("target", "bin")], web.CreatedDirectories);
        Assert.Empty(plan.Warnings);
        Assert.Empty(plan.Errors);
    }

    [Fact]
    public void Create_PackageOfAnotherToolVersion_Warns()
    {
        using var temp = new TempDirectory();
        temp.Write("target/App.dll", "old");
        string target = temp.Combine("target");
        var package = new PackageBuilder(temp, PackageName) { ToolVersion = "0.0.1" }
            .Project("Web", PreviousPackage, false, Files(("App.dll", "new")), null, Files(("App.dll", "old")))
            .Build();

        var plan = Plan(package, target);

        Assert.Contains(plan.Warnings, w => w.Code == WarningCodes.ToolVersion && w.Message.Contains("0.0.1"));
    }

    [Fact]
    public void Create_MissingTargetDirectory_Warns()
    {
        using var temp = new TempDirectory();
        string target = temp.Combine("missing");

        var plan = Plan(WebPackage(temp, null, true), target, new ServerConfig());

        Assert.Equal(WarningCodes.TargetMissing, Assert.Single(plan.Warnings).Code);
    }

    [Theory]
    [InlineData(PreviousPackage, PreviousPackage, false, null)]
    [InlineData(PackageName, PreviousPackage, false, WarningCodes.AlreadyDeployed)]
    [InlineData("uat_2025-12-31_12-00-00", PreviousPackage, false, WarningCodes.ChainMismatch)]
    [InlineData(null, PreviousPackage, false, WarningCodes.NoRecord)]
    [InlineData(PreviousPackage, null, false, WarningCodes.UnknownBase)]
    [InlineData(PreviousPackage, null, true, WarningCodes.FullPublish)]
    [InlineData(null, null, true, null)]
    public void Create_ChecksTheDeployChain(string? recorded, string? basePackage, bool isFullPublish, string? expectedCode)
    {
        using var temp = new TempDirectory();
        temp.Write("target/Other.dll", "old");
        string target = temp.Combine("target");
        var config = recorded is null ? new ServerConfig() : RecordedAt(target, recorded);

        var plan = Plan(WebPackage(temp, basePackage, isFullPublish), target, config);

        if (expectedCode is null)
        {
            Assert.Empty(plan.Warnings);
        }
        else
        {
            Assert.Equal(expectedCode, Assert.Single(plan.Warnings).Code);
        }
    }

    [Fact]
    public void Create_TwoProjectsOnOnePath_IsAnError()
    {
        using var temp = new TempDirectory();
        temp.Write("target/App.dll", "old");
        string target = temp.Combine("target");
        var package = new PackageBuilder(temp, PackageName)
            .Project("Web", null, true, Files(("App.dll", "new")))
            .Project("Api", null, true, Files(("Api.dll", "new")))
            .Build();

        var plan = DeployPlan.Create(package, Map(("Web", target, null), ("Api", target, null)), new ServerConfig(), new FakeHostController(),
            BuildInfo.Version, CancellationToken.None);

        Assert.Contains("are all mapped to", Assert.Single(plan.Errors));
    }

    [Fact]
    public void Create_MissingServiceAndIis_AreErrors()
    {
        using var temp = new TempDirectory();
        temp.Write("web/App.dll", "old");
        temp.Write("worker/Worker.dll", "old");
        var package = new PackageBuilder(temp, PackageName)
            .Project("Web", null, true, Files(("App.dll", "new")))
            .Project("Worker", null, true, Files(("Worker.dll", "new")))
            .Build();
        var hosts = new FakeHostController { IisInstalled = false };

        var plan = DeployPlan.Create(package, Map(("Web", temp.Combine("web"), null), ("Worker", temp.Combine("worker"), "WorkerService")),
            new ServerConfig(), hosts, BuildInfo.Version, CancellationToken.None);

        Assert.Equal(2, plan.Errors.Count);
        Assert.Contains(plan.Errors, e => e.Contains("IIS"));
        Assert.Contains(plan.Errors, e => e.Contains("WorkerService"));
    }

    [Fact]
    public void Create_OverwrittenAndDeletedConfigFiles_WarnConfigChanged()
    {
        using var temp = new TempDirectory();
        temp.Write("target/web.config", "old web");
        temp.Write("target/Old.dll.config", "old settings");
        temp.Write("target/App.dll", "old app");
        string target = temp.Combine("target");
        var package = new PackageBuilder(temp, PackageName)
            .Project("Web", PreviousPackage, false,
                Files(("web.config", "new web"), ("appsettings.Production.json", "new"), ("App.dll", "new app")),
                Files(("Old.dll.config", "old settings")),
                Files(("web.config", "old web"), ("App.dll", "old app")))
            .Build();

        var plan = Plan(package, target);

        var warning = Assert.Single(plan.Warnings);
        Assert.Equal(WarningCodes.ConfigChanged, warning.Code);
        Assert.Equal(["web.config", "Old.dll.config"], warning.Files);
        Assert.Contains("Old.dll.config (deleted)", warning.Message);
        Assert.DoesNotContain("appsettings.Production.json", warning.Message);
    }

    [Theory]
    [InlineData("web.config", true)]
    [InlineData("Views/Web.Config", true)]
    [InlineData("Trw.Ldp.PrintJobProcessor.exe.config", true)]
    [InlineData("Trw.Ldp.BarTenderAdapter.dll.config", true)]
    [InlineData("appsettings.json", true)]
    [InlineData("APPSETTINGS.Production.JSON", true)]
    [InlineData("web.config.bak", false)]
    [InlineData("App.config", false)]
    [InlineData("settings.json", false)]
    [InlineData("App.dll", false)]
    public void IsConfigFile_MatchesTheBuiltInPatterns(string path, bool expected)
    {
        Assert.Equal(expected, ConfigFiles.IsConfigFile(path));
    }

    [Theory]
    [InlineData("old", false)]
    [InlineData("new", false)]
    [InlineData("edited on the server", true)]
    public void Create_OverwrittenFile_WarnsWhenItIsNeitherThePreviousNorTheNewContent(string onTarget, bool warns)
    {
        using var temp = new TempDirectory();
        temp.Write("target/App.dll", onTarget);
        string target = temp.Combine("target");
        var package = new PackageBuilder(temp, PackageName)
            .Project("Web", PreviousPackage, false, Files(("App.dll", "new")), null, Files(("App.dll", "old")))
            .Build();

        var plan = Plan(package, target);

        Assert.Equal(warns, plan.Warnings.Any(w => w.Code == WarningCodes.TargetModified && w.Files is ["App.dll"]));
    }

    [Theory]
    [InlineData("new", false)]
    [InlineData("server only", true)]
    public void Create_FileNewInThePublishButOnTheTarget_WarnsUnlessItHasTheNewContent(string onTarget, bool warns)
    {
        using var temp = new TempDirectory();
        temp.Write("target/App.dll", onTarget);
        string target = temp.Combine("target");
        var package = new PackageBuilder(temp, PackageName)
            .Project("Web", PreviousPackage, false, Files(("App.dll", "new")))
            .Build();

        var plan = Plan(package, target);

        Assert.Equal(warns, plan.Warnings.Any(w => w.Code == WarningCodes.TargetModified));
    }

    [Theory]
    [InlineData("old", false)]
    [InlineData("edited on the server", true)]
    public void Create_DeletedFile_WarnsWhenItIsNotTheRecordedContent(string onTarget, bool warns)
    {
        using var temp = new TempDirectory();
        temp.Write("target/Old.dll", onTarget);
        string target = temp.Combine("target");
        var package = new PackageBuilder(temp, PackageName)
            .Project("Web", PreviousPackage, false, Files(), Files(("Old.dll", "old")))
            .Build();

        var plan = Plan(package, target);

        Assert.Equal(warns, plan.Warnings.Any(w => w.Code == WarningCodes.TargetModified && w.Files is ["Old.dll"]));
    }

    [Fact]
    public void Create_TargetModified_RunsAlsoWhenTheChainCheckWarns()
    {
        using var temp = new TempDirectory();
        temp.Write("target/web.config", "hand maintained");
        string target = temp.Combine("target");
        var package = new PackageBuilder(temp, PackageName)
            .Project("Web", PreviousPackage, false, Files(("web.config", "new")), null, Files(("web.config", "old")))
            .Build();

        var plan = Plan(package, target, new ServerConfig());

        Assert.Equal([WarningCodes.NoRecord, WarningCodes.ConfigChanged, WarningCodes.TargetModified], plan.Warnings.Select(w => w.Code));
    }

    [Fact]
    public void Create_ManyModifiedFiles_CapsTheMessageButKeepsEveryFile()
    {
        using var temp = new TempDirectory();
        var files = Enumerable.Range(1, DeployWarning.MaxListedFiles + 2).Select(i => ($"Lib{i:00}.dll", "new")).ToArray();
        foreach (var (path, _) in files)
        {
            temp.Write($"target/{path}", "edited");
        }

        var package = new PackageBuilder(temp, PackageName)
            .Project("Web", PreviousPackage, false, Files(files), null, Files([.. files.Select(f => (f.Item1, "old"))]))
            .Build();

        var plan = Plan(package, temp.Combine("target"));

        var warning = Assert.Single(plan.Warnings);
        Assert.Equal(files.Length, warning.Files?.Count);
        Assert.EndsWith("and 2 more.", warning.Message);
    }

    [Fact]
    public void Create_UnreadableTargetFile_IsReportedAsTargetModified()
    {
        using var temp = new TempDirectory();
        string locked = temp.Write("target/App.dll", "old");
        var package = new PackageBuilder(temp, PackageName)
            .Project("Web", PreviousPackage, false, Files(("App.dll", "new")), null, Files(("App.dll", "old")))
            .Build();
        using var lockStream = new FileStream(locked, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        var plan = Plan(package, temp.Combine("target"));

        var warning = Assert.Single(plan.Warnings);
        Assert.Equal(WarningCodes.TargetModified, warning.Code);
        Assert.Contains("App.dll (could not be read)", warning.Message);
    }
}
