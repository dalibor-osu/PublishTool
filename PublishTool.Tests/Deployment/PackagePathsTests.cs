using PublishTool.Deployment;
using PublishTool.Tests.Support;

namespace PublishTool.Tests.Deployment;

public class PackagePathsTests
{
    [Fact]
    public void Resolve_NestedManifestPath_ReturnsPathInsideRoot()
    {
        using var temp = new TempDirectory();

        string resolved = PackagePaths.Resolve(temp.Path, "bin/App.dll");

        Assert.Equal(Path.Combine(temp.Path, "bin", "App.dll"), resolved);
    }

    [Theory]
    [InlineData("../outside.txt")]
    [InlineData("bin/../../outside.txt")]
    [InlineData("")]
    public void Resolve_PathOutsideRoot_Throws(string manifestPath)
    {
        using var temp = new TempDirectory();

        Assert.Throws<DeployException>(() => PackagePaths.Resolve(temp.Path, manifestPath));
    }

    [Fact]
    public void Resolve_RootedPath_Throws()
    {
        using var temp = new TempDirectory();

        Assert.Throws<DeployException>(() => PackagePaths.Resolve(temp.Path, Path.GetFullPath(temp.Combine("file.txt"))));
    }

    [Fact]
    public void ToManifest_UsesForwardSlashes()
    {
        Assert.Equal("bin/sub/App.dll", PackagePaths.ToManifest(@"bin\sub\App.dll"));
    }
}
