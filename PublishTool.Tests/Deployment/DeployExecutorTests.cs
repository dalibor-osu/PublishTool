using PublishTool.Configuration;
using PublishTool.Deployment;
using PublishTool.Tests.Support;

namespace PublishTool.Tests.Deployment;

public class DeployExecutorTests
{
    private const string PreviousPackage = "uat_2026-01-01_12-00-00";

    private static DeployPlan CreatePlan(TempDirectory temp)
    {
        temp.Write("target/App.dll", "old app");
        temp.Write("target/Old.dll", "old file");
        temp.Write("target/sub/Gone.dll", "gone");
        string target = temp.Combine("target");
        var package = new PackageBuilder(temp)
            .Project("Web", PreviousPackage, false,
                new Dictionary<string, string> { ["App.dll"] = "new app", ["bin/New.dll"] = "new file" },
                new Dictionary<string, string> { ["Old.dll"] = "old file", ["sub/Gone.dll"] = "gone" },
                new Dictionary<string, string> { ["App.dll"] = "old app" })
            .Build();
        var config = new ServerConfig();
        config.SetRecord(target, new DeployRecord { Package = PreviousPackage });
        return DeployPlan.Create(package, new Dictionary<string, DeployTarget> { ["Web"] = new() { Path = target } }, config,
            new FakeHostController(), BuildInfo.Version, CancellationToken.None);
    }

    [Fact]
    public void BackupApplyRestore_LeavesTheTargetAsBefore()
    {
        using var temp = new TempDirectory();
        var plan = CreatePlan(temp);
        var journal = DeployExecutor.CreateJournal(plan, BuildInfo.Version);

        int backedUp = DeployExecutor.Backup(plan, journal, _ => { }, CancellationToken.None);
        var (copied, deleted) = DeployExecutor.Apply(plan, _ => { }, CancellationToken.None);

        Assert.Equal(3, backedUp);
        Assert.Equal((2, 2), (copied, deleted));
        Assert.Equal("new app", temp.Read("target/App.dll"));
        Assert.True(temp.Exists("target/bin/New.dll"));
        Assert.False(temp.Exists("target/Old.dll"));
        Assert.False(Directory.Exists(temp.Combine("target", "sub")));

        var errors = DeployExecutor.Restore(plan.Package.BackupDirectory, journal, _ => { });

        Assert.Empty(errors);
        Assert.Equal("old app", temp.Read("target/App.dll"));
        Assert.Equal("old file", temp.Read("target/Old.dll"));
        Assert.Equal("gone", temp.Read("target/sub/Gone.dll"));
        Assert.False(Directory.Exists(temp.Combine("target", "bin")));
    }

    [Fact]
    public void Backup_ExistingBackupDirectory_Throws()
    {
        using var temp = new TempDirectory();
        var plan = CreatePlan(temp);
        Directory.CreateDirectory(plan.Package.BackupDirectory);

        Assert.Throws<DeployException>(() =>
            DeployExecutor.Backup(plan, DeployExecutor.CreateJournal(plan, BuildInfo.Version), _ => { }, CancellationToken.None));
    }

    [Fact]
    public void Verify_TamperedFile_IsReported()
    {
        using var temp = new TempDirectory();
        var plan = CreatePlan(temp);
        File.WriteAllText(plan.Package.FilePath(plan.Package.Manifest.Projects[0], "App.dll"), "tampered");
        temp.Write($"packages/{plan.Package.Manifest.Name}/Web/extra.txt", "extra");

        var verification = plan.Package.Verify(_ => { }, CancellationToken.None);

        Assert.Contains("Web/App.dll", Assert.Single(verification.Errors));
        Assert.Equal(["Web/extra.txt"], verification.ExtraFiles);
    }
}
