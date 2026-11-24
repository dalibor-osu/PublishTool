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

    private static DeployPackage WebPackage(TempDirectory temp, string? basePackage = PreviousPackage, bool isFullPublish = false) =>
        new PackageBuilder(temp, PackageName)
            .Project("Web", basePackage, isFullPublish, new Dictionary<string, string> { ["App.dll"] = "new", ["bin/New.dll"] = "new" }, ["Old.dll"])
            .Build();

    private static ServerConfig RecordedAt(string targetPath, string package)
    {
        var config = new ServerConfig();
        config.SetRecord(targetPath, new DeployRecord { Package = package });
        return config;
    }

    [Fact]
    public void Create_SortsFilesIntoCreateOverwriteAndDelete()
    {
        using var temp = new TempDirectory();
        temp.Write("target/App.dll", "old");
        temp.Write("target/Old.dll", "old");
        string target = temp.Combine("target");

        var plan = DeployPlan.Create(WebPackage(temp), Map(("Web", target, null)), RecordedAt(target, PreviousPackage), new FakeHostController(),
            BuildInfo.Version);

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
            .Project("Web", PreviousPackage, false, new Dictionary<string, string> { ["App.dll"] = "new" })
            .Build();

        var plan = DeployPlan.Create(package, Map(("Web", target, null)), RecordedAt(target, PreviousPackage), new FakeHostController(),
            BuildInfo.Version);

        Assert.Contains(plan.Warnings, w => w.Code == WarningCodes.ToolVersion && w.Message.Contains("0.0.1"));
    }

    [Fact]
    public void Create_MissingTargetDirectory_Warns()
    {
        using var temp = new TempDirectory();
        string target = temp.Combine("missing");

        var plan = DeployPlan.Create(WebPackage(temp, null, true), Map(("Web", target, null)), new ServerConfig(), new FakeHostController(),
            BuildInfo.Version);

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
        temp.Write("target/App.dll", "old");
        string target = temp.Combine("target");
        var config = recorded is null ? new ServerConfig() : RecordedAt(target, recorded);

        var plan = DeployPlan.Create(WebPackage(temp, basePackage, isFullPublish), Map(("Web", target, null)), config,
            new FakeHostController(), BuildInfo.Version);

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
            .Project("Web", null, true, new Dictionary<string, string> { ["App.dll"] = "new" })
            .Project("Api", null, true, new Dictionary<string, string> { ["Api.dll"] = "new" })
            .Build();

        var plan = DeployPlan.Create(package, Map(("Web", target, null), ("Api", target, null)), new ServerConfig(), new FakeHostController(),
            BuildInfo.Version);

        Assert.Contains("are all mapped to", Assert.Single(plan.Errors));
    }

    [Fact]
    public void Create_MissingServiceAndIis_AreErrors()
    {
        using var temp = new TempDirectory();
        temp.Write("web/App.dll", "old");
        temp.Write("worker/Worker.dll", "old");
        var package = new PackageBuilder(temp, PackageName)
            .Project("Web", null, true, new Dictionary<string, string> { ["App.dll"] = "new" })
            .Project("Worker", null, true, new Dictionary<string, string> { ["Worker.dll"] = "new" })
            .Build();
        var hosts = new FakeHostController { IisInstalled = false };

        var plan = DeployPlan.Create(package, Map(("Web", temp.Combine("web"), null), ("Worker", temp.Combine("worker"), "WorkerService")),
            new ServerConfig(), hosts, BuildInfo.Version);

        Assert.Equal(2, plan.Errors.Count);
        Assert.Contains(plan.Errors, e => e.Contains("IIS"));
        Assert.Contains(plan.Errors, e => e.Contains("WorkerService"));
    }
}
