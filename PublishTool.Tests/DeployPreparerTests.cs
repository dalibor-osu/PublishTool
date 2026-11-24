using System.IO.Compression;
using PublishTool.Tests.Support;

namespace PublishTool.Tests;

public class DeployPreparerTests
{
    private const string FirstName = "uat_2026-01-01_12-00-00";
    private const string SecondName = "uat_2026-01-02_12-00-00";

    private static DeployPreparer Preparer(TempDirectory temp, params string[] projects) =>
        new(temp.Combine("shared"), temp.Combine("deploy"), temp.Combine("publish"), projects);

    private static void PublishFirst(TempDirectory temp)
    {
        temp.Write("publish/Web/App.dll", "app v1");
        temp.Write("publish/Web/Old.dll", "old");
        temp.Write("publish/Api/Api.dll", "api v1");
        var preparer = Preparer(temp, "Web", "Api");
        var changes = preparer.CollectChanges(preparer.DeployPath(FirstName), null, FirstName, _ => { }, CancellationToken.None);
        var archive = preparer.CreateArchive(preparer.ArchivePath(FirstName), null, changes.Lineage, _ => { }, CancellationToken.None);
        string uploading = DeployPreparer.Upload(archive.Path, preparer.ReferencePath(FirstName), _ => { }, CancellationToken.None);
        DeployPreparer.Commit(uploading, preparer.ReferencePath(FirstName));
    }

    [Fact]
    public void CollectChanges_WithoutPrevious_TreatsEveryProjectAsFullPublish()
    {
        using var temp = new TempDirectory();
        temp.Write("publish/Web/App.dll", "app");
        var preparer = Preparer(temp, "Web");

        var changes = preparer.CollectChanges(preparer.DeployPath(FirstName), null, FirstName, _ => { }, CancellationToken.None);

        var web = Assert.Single(changes.Projects);
        Assert.True(web.IsFullPublish);
        Assert.Null(web.Base);
        var app = Assert.Single(web.Files);
        Assert.Equal("App.dll", app.Path);
        Assert.Null(app.PreviousSha256);
        Assert.Equal(FirstName, changes.Lineage.Projects["Web"]);
        Assert.True(temp.Exists($"deploy/{FirstName}/Web/App.dll"));
    }

    [Fact]
    public void CollectChanges_WithPrevious_KeepsOnlyChangesDeletionsAndLineage()
    {
        using var temp = new TempDirectory();
        PublishFirst(temp);
        File.Delete(temp.Combine("publish", "Web", "Old.dll"));
        temp.Write("publish/Web/App.dll", "app v2");
        var preparer = Preparer(temp, "Web");
        string reference = Assert.IsType<string>(preparer.FindPreviousReference("uat"));
        var (previous, _) = DeployPreparer.Download(reference, preparer.WorkPath(SecondName), _ => { }, CancellationToken.None);

        var changes = preparer.CollectChanges(preparer.DeployPath(SecondName), previous, SecondName, _ => { }, CancellationToken.None);
        var archive = preparer.CreateArchive(preparer.ArchivePath(SecondName), previous, changes.Lineage, _ => { }, CancellationToken.None);

        var web = Assert.Single(changes.Projects);
        Assert.False(web.IsFullPublish);
        Assert.Equal(FirstName, web.Base);
        var app = Assert.Single(web.Files);
        Assert.Equal("App.dll", app.Path);
        Assert.Equal(PackageBuilder.Sha256Of("app v2"), app.Sha256);
        Assert.Equal(PackageBuilder.Sha256Of("app v1"), app.PreviousSha256);
        var deleted = Assert.Single(web.Deleted);
        Assert.Equal("Old.dll", deleted.Path);
        Assert.Equal(PackageBuilder.Sha256Of("old"), deleted.Sha256);
        Assert.Equal(SecondName, changes.Lineage.Projects["Web"]);
        Assert.Equal(FirstName, changes.Lineage.Projects["Api"]);
        Assert.Equal(["Api"], archive.Carried);
        using var zip = ZipFile.OpenRead(archive.Path);
        Assert.Contains(zip.Entries, e => e.FullName == "Api/Api.dll");
        Assert.Contains(zip.Entries, e => e.FullName == "publishtool-lineage.json");
    }

    [Fact]
    public void FindPreviousReference_PicksTheNewestArchiveOfThePrefix()
    {
        using var temp = new TempDirectory();
        temp.Write($"shared/{FirstName}.zip", string.Empty);
        temp.Write($"shared/{SecondName}.zip", string.Empty);
        temp.Write("shared/prod_2026-02-01_12-00-00.zip", string.Empty);

        string? reference = Preparer(temp).FindPreviousReference("uat");

        Assert.Equal(temp.Combine("shared", $"{SecondName}.zip"), reference);
    }

    [Fact]
    public void FindPreviousReference_NewerLegacyDirectory_Throws()
    {
        using var temp = new TempDirectory();
        temp.Write($"shared/{FirstName}.zip", string.Empty);
        Directory.CreateDirectory(temp.Combine("shared", SecondName));

        Assert.Throws<InvalidOperationException>(() => Preparer(temp).FindPreviousReference("uat"));
    }

    [Fact]
    public void PruneReferences_KeepsTheNewestArchivesOfThePrefixOnly()
    {
        using var temp = new TempDirectory();
        temp.Write($"shared/{FirstName}.zip", string.Empty);
        temp.Write($"shared/{SecondName}.zip", string.Empty);
        temp.Write("shared/uat_2026-01-03_12-00-00.zip", string.Empty);
        temp.Write("shared/prod_2025-01-01_12-00-00.zip", string.Empty);

        var deleted = Preparer(temp).PruneReferences("uat", 2, _ => { }, CancellationToken.None);

        Assert.Equal([$"{FirstName}.zip"], deleted);
        Assert.True(temp.Exists("shared/prod_2025-01-01_12-00-00.zip"));
    }

    [Fact]
    public void SelectByName_ReportsUnknownAndAmbiguousNames()
    {
        DotnetProject[] projects =
        [
            new() { AbsolutePath = @"C:\repo\Web\Web.csproj", Name = "Web", Kind = ProjectKind.Sdk },
            new() { AbsolutePath = @"C:\repo\Api\Api.csproj", Name = "Api", Kind = ProjectKind.Sdk },
            new() { AbsolutePath = @"C:\repo\copy\Api\Api.csproj", Name = "Api", Kind = ProjectKind.Sdk }
        ];
        var problems = new List<string>();

        var selected = ProjectScanner.SelectByName(projects, ["web", "Api", "Missing"], problems);

        Assert.Equal("Web", Assert.Single(selected).Name);
        Assert.Equal(2, problems.Count);
    }

    [Fact]
    public void DirectoryName_UsesThePrefixAndTimestamp()
    {
        Assert.Equal(FirstName, DeployPreparer.DirectoryName("uat", new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero)));
    }
}
