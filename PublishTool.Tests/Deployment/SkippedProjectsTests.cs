using System.Text.Json;
using PublishTool.Configuration;
using PublishTool.Deployment;
using PublishTool.Tests.Support;

namespace PublishTool.Tests.Deployment;

public class SkippedProjectsTests
{
    private static ServerConfig WebOnThisServer()
    {
        var config = new ServerConfig();
        var environment = config.EnvironmentFor("uat");
        environment.Targets["Web"] = new DeployTarget { Path = @"C:\sites\web" };
        environment.Skipped.Add("Api");
        return config;
    }

    private static DeployPackage WebAndApi(TempDirectory temp) =>
        new PackageBuilder(temp)
            .Project("Web", null, true, new Dictionary<string, string> { ["App.dll"] = "web" })
            .Project("Api", null, true, new Dictionary<string, string> { ["Api.dll"] = "api", ["bin/Lib.dll"] = "lib" })
            .Build();

    [Fact]
    public void Resolve_StoredSkip_IsSkippedNotMissing()
    {
        var resolution = DeployTargets.Resolve(WebOnThisServer(), "UAT", ["Web", "api"]);

        Assert.Equal(["Web"], resolution.Targets.Keys);
        Assert.Empty(resolution.Missing);
        Assert.Equal(["api"], resolution.Skipped);
    }

    [Fact]
    public void Resolve_TargetOverride_WinsOverStoredSkip()
    {
        var overrides = new Dictionary<string, TargetOverride> { ["Api"] = new() { Path = @"D:\api" } };

        var resolution = DeployTargets.Resolve(WebOnThisServer(), "uat", ["Web", "Api"], overrides);

        Assert.Equal(@"D:\api", resolution.Targets["Api"].Path);
        Assert.Empty(resolution.Skipped);
    }

    [Fact]
    public void Resolve_RunSkip_SkipsStoredTargetsAndMissingOnes()
    {
        var resolution = DeployTargets.Resolve(WebOnThisServer(), "uat", ["Web", "Worker"], skip: ["WEB", "worker"]);

        Assert.Empty(resolution.Targets);
        Assert.Empty(resolution.Missing);
        Assert.Equal(["Web", "Worker"], resolution.Skipped);
    }

    [Fact]
    public void Skip_RemovesTheTargetAndUnskipUndoesIt()
    {
        var environment = WebOnThisServer().EnvironmentFor("uat");

        environment.Skip("web");

        Assert.Empty(environment.Targets);
        Assert.True(environment.IsSkipped("WEB"));
        Assert.True(environment.Unskip("Web"));
        Assert.False(environment.Unskip("Web"));
        Assert.Equal(["Api"], environment.Skipped);
    }

    [Fact]
    public void Skipped_SurvivesJsonRoundTripAndOldFilesReadAsEmpty()
    {
        string json = JsonSerializer.Serialize(WebOnThisServer(), JsonContext.Default.ServerConfig);

        var read = JsonSerializer.Deserialize(json, JsonContext.Default.ServerConfig)!;
        var old = JsonSerializer.Deserialize("""{"Version":1,"Environments":{"uat":{"Targets":{}}}}""", JsonContext.Default.ServerConfig)!;

        Assert.Equal(["Api"], read.Environments["uat"].Skipped);
        Assert.Empty(old.Environments["uat"].Skipped);
    }

    [Fact]
    public void WithoutProjects_VerifiesOnlyDeployedProjectsWhenSkippedOnesWereNotCopied()
    {
        using var temp = new TempDirectory();
        var package = WebAndApi(temp);
        Directory.Delete(Path.Combine(package.Directory, "Api"), true);

        var filtered = package.WithoutProjects(["api"]);
        var verification = filtered.Verify(_ => { }, CancellationToken.None);

        Assert.Equal(["Web"], filtered.Manifest.Projects.Select(p => p.Name));
        Assert.Equal(["Api"], filtered.SkippedProjects);
        Assert.Equal(1, filtered.FileCount);
        Assert.Empty(verification.Errors);
        Assert.Empty(verification.ExtraFiles);
        Assert.Equal(2, package.Manifest.Projects.Count);
    }

    [Fact]
    public void WithoutProjects_CopiedSkippedProjectIsNotReportedAsExtraFiles()
    {
        using var temp = new TempDirectory();

        var verification = WebAndApi(temp).WithoutProjects(["Api"]).Verify(_ => { }, CancellationToken.None);

        Assert.Empty(verification.Errors);
        Assert.Empty(verification.ExtraFiles);
    }

    [Fact]
    public void Plan_EveryProjectSkipped_IsAllSkipped()
    {
        using var temp = new TempDirectory();
        var package = WebAndApi(temp).WithoutProjects(["Web", "Api"]);

        var plan = DeployPlan.Create(package, new Dictionary<string, DeployTarget>(), new ServerConfig(), new FakeHostController(),
            BuildInfo.Version, CancellationToken.None);

        Assert.True(plan.IsAllSkipped);
        Assert.Empty(plan.Targets);
        Assert.Empty(plan.Errors);
    }

    [Fact]
    public void Plan_SomeProjectsSkipped_PlansOnlyTheOthers()
    {
        using var temp = new TempDirectory();
        string target = temp.Combine("web");
        var package = WebAndApi(temp).WithoutProjects(["Api"]);
        var targets = new Dictionary<string, DeployTarget> { ["Web"] = new() { Path = target } };

        var plan = DeployPlan.Create(package, targets, new ServerConfig(), new FakeHostController(), BuildInfo.Version, CancellationToken.None);

        Assert.False(plan.IsAllSkipped);
        Assert.Equal("Web", Assert.Single(plan.Targets).Project.Name);
    }
}
