using System.Text.Json;
using PublishTool.GitHub;
using PublishTool.Helpers;

namespace PublishTool.Tests.GitHub;

public class SelfUpdateTests
{
    private static readonly string Rid = $"{EnvironmentHelper.GetCurrentOsShortcut()}-{EnvironmentHelper.GetCurrentArchitecture()}";
    private static readonly string Extension = EnvironmentHelper.GetPlatformArchiveExtension();

    private static ReleaseVersion Release(params string[] assets)
    {
        string list = string.Join(", ", assets.Select(a => $$"""{ "name": "{{a}}", "browser_download_url": "https://example.test/{{a}}" }"""));
        using var document = JsonDocument.Parse($$"""{ "tag_name": "v1.4.0", "name": "v1.4.0", "id": 7, "assets": [{{list}}] }""");
        return ReleaseVersion.Parse(document.RootElement.Clone()).Value;
    }

    [Fact]
    public void AssetName_TuiAndCliUseTheirOwnLayout()
    {
        var release = Release();

        Assert.Equal($"PublishTool-v1.4.0-{Rid}.{Extension}", SelfUpdate.Tui.AssetName(release));
        Assert.Equal($"PublishToolCli-{Rid}-v1.4.0.{Extension}", SelfUpdate.Cli.AssetName(release));
    }

    [Fact]
    public void CliAssetName_IsNotMatchedByTheSuffixRuleOfOlderTuiVersions()
    {
        Assert.False(SelfUpdate.Cli.AssetName(Release()).EndsWith($"{Rid}.{Extension}", StringComparison.Ordinal));
    }

    [Fact]
    public void FindAssetUrl_MatchesTheExactAssetName()
    {
        string tui = $"PublishTool-v1.4.0-{Rid}.{Extension}";
        string cli = $"PublishToolCli-{Rid}-v1.4.0.{Extension}";
        var release = Release(cli, tui);

        Assert.Equal($"https://example.test/{tui}", release.FindAssetUrl(SelfUpdate.Tui.AssetName(release)));
        Assert.Equal($"https://example.test/{cli}", release.FindAssetUrl(SelfUpdate.Cli.AssetName(release)));
        Assert.Empty(release.FindAssetUrl("PublishTool-v9.9.9-none.zip"));
    }
}
