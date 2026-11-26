using PublishTool.Deployment;

namespace PublishTool.Tests.Deployment;

public class KeepPatternsTests
{
    [Theory]
    [InlineData("logs/**", "logs", true)]
    [InlineData("logs/**", "logs/app.log", true)]
    [InlineData("logs/**", "logs/2026/01/app.log", true)]
    [InlineData("logs/**", "bin/logs/app.log", false)]
    [InlineData("logs/**", "logs.txt", false)]
    [InlineData("logs", "bin/logs/app.log", true)]
    [InlineData("*.log", "app.log", true)]
    [InlineData("*.log", "deep/er/APP.LOG", true)]
    [InlineData("*.log", "app.log.bak", false)]
    [InlineData("App_Data", "App_Data/uploads/a.png", true)]
    [InlineData("wwwroot/uploads", "wwwroot/uploads/a.png", true)]
    [InlineData("wwwroot/uploads", "uploads/a.png", false)]
    [InlineData("wwwroot/*.map", "wwwroot/site.map", true)]
    [InlineData("wwwroot/*.map", "wwwroot/js/site.map", false)]
    [InlineData("wwwroot/**/*.map", "wwwroot/js/site.map", true)]
    [InlineData("data?.bin", "data1.bin", true)]
    [InlineData("data?.bin", "data12.bin", false)]
    [InlineData(@"logs\**", "logs/app.log", true)]
    public void IsKept_MatchesFilesDirectoriesAndEverythingBelowThem(string pattern, string path, bool expected)
    {
        Assert.Equal(expected, new KeepPatterns([pattern]).IsKept(path));
    }

    [Fact]
    public void IsKept_WithoutPatterns_KeepsNothing()
    {
        Assert.False(new KeepPatterns([]).IsKept("logs/app.log"));
    }

    [Theory]
    [InlineData(@" logs\** ", "logs/**")]
    [InlineData("logs/", "logs")]
    [InlineData("*.log", "*.log")]
    public void TryNormalize_AcceptsRelativePatterns(string value, string expected)
    {
        Assert.True(KeepPatterns.TryNormalize(value, out string pattern, out string? error));
        Assert.Equal(expected, pattern);
        Assert.Null(error);
    }

    [Theory]
    [InlineData("")]
    [InlineData("/logs")]
    [InlineData(@"C:\sites\web\logs")]
    [InlineData("../other")]
    [InlineData("logs/../../other")]
    [InlineData("logs//x")]
    public void TryNormalize_RejectsEmptyRootedAndParentPatterns(string value)
    {
        Assert.False(KeepPatterns.TryNormalize(value, out _, out string? error));
        Assert.NotNull(error);
    }
}
