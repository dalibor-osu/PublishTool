using System.Xml.Linq;

namespace PublishTool.Tests;

public class MsBuildPropertiesTests
{
    private const string Prefix = "uat";

    private static DotnetProject Project(ProjectKind kind) =>
        new() { AbsolutePath = $@"C:\repo\{kind}\{kind}.csproj", Name = kind.ToString(), Kind = kind };

    [Theory]
    [InlineData(ProjectKind.Sdk)]
    [InlineData(ProjectKind.LegacyWeb)]
    [InlineData(ProjectKind.LegacyLibrary)]
    public void BuildCommand_PassesThePrefixForEveryProjectKind(ProjectKind kind)
    {
        string command = new PublishCommandBuilder(Project(kind), "Release", @"C:\publish\Web").BuildCommand(Prefix);

        string[] properties = [.. command.Split(' ').Where(a => a.StartsWith($"/p:{MsBuildProperties.Prefix}=", StringComparison.Ordinal))];
        Assert.Equal([$"/p:{MsBuildProperties.Prefix}={Prefix}"], properties);
    }

    [Fact]
    public void TraversalProject_PassesThePrefixToRestoreAndBuild()
    {
        string path = DependencyBuild.WriteTraversalProject([Project(ProjectKind.Sdk), Project(ProjectKind.LegacyWeb)], "Release", Prefix);
        try
        {
            var tasks = XDocument.Load(path).Descendants("MSBuild").ToList();

            Assert.Equal(2, tasks.Count);
            Assert.All(tasks, task =>
            {
                string[] properties = ((string?)task.Attribute("Properties") ?? string.Empty).Split(';');
                Assert.Contains("Configuration=Release", properties);
                Assert.Contains($"{MsBuildProperties.Prefix}={Prefix}", properties);
            });
        }
        finally
        {
            DependencyBuild.TryDelete(path);
        }
    }

    [Theory]
    [InlineData("uat")]
    [InlineData("DEPLOY")]
    [InlineData("wip-LDPNG-130-add-report")]
    [InlineData("release-1.2+hotfix_x")]
    public void PrefixProblem_AcceptsPlainBranchNames(string prefix)
    {
        Assert.Null(MsBuildProperties.PrefixProblem(prefix));
    }

    [Theory]
    [InlineData("")]
    [InlineData("a;b")]
    [InlineData("a,b")]
    [InlineData("a=b")]
    [InlineData("a&b")]
    [InlineData("a%3Bb")]
    [InlineData("a$(b)")]
    [InlineData("a@b")]
    [InlineData("a'b")]
    [InlineData("a b")]
    public void PrefixProblem_RejectsCharactersSpecialToMsBuild(string prefix)
    {
        Assert.NotNull(MsBuildProperties.PrefixProblem(prefix));
    }
}
