using System.Text.Json;
using PublishTool.GitHub;

namespace PublishTool.Tests.GitHub;

public class ReleaseVersionTests
{
    private static ReleaseVersion Release(string tag, long id = 1)
    {
        using var document = JsonDocument.Parse($$"""{ "tag_name": "{{tag}}", "name": "{{tag}}", "id": {{id}}, "assets": [] }""");
        var result = ReleaseVersion.Parse(document.RootElement.Clone());
        Assert.True(result.IsSuccess);
        return result.Value;
    }

    [Theory]
    [InlineData("v1.2.3", "v1.2.4")]
    [InlineData("v1.2.3-rc.1", "v1.2.3")]
    [InlineData("v1.2.3-rc.1", "v1.2.3-rc.2")]
    [InlineData("v1.9.9", "v2.0.0")]
    public void CompareTo_OrdersBySemanticVersion(string older, string newer)
    {
        Assert.True(Release(older, 1) < Release(newer, 2));
    }

    [Fact]
    public void Parse_ReleaseCandidate_IsPreRelease()
    {
        var version = Release("v1.0.0-rc.3");

        Assert.True(version.IsPreRelease);
        Assert.Equal(3u, version.RcNumber);
    }
}
