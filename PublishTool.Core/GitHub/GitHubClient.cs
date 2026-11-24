using System.Net.Http.Headers;
using System.Text.Json;

namespace PublishTool.GitHub;

public class GitHubClient
{
    private const string RepositoryApiUrl = "https://api.github.com/repos/dalibor-osu/PublishTool/";
    private readonly HttpClient _httpClient;

    public GitHubClient()
    {
        _httpClient = new HttpClient { BaseAddress = new Uri(RepositoryApiUrl) };
        _httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("PublishTool", BuildInfo.Version));
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github.v3+json"));
    }

    public Task<Result<ReleaseVersion>> GetLatestFullVersionAsync(CancellationToken ct) => GetLatestVersionAsync(false, ct);

    public Task<Result<ReleaseVersion>> GetLatestVersionIncludingPreReleaseAsync(CancellationToken ct) => GetLatestVersionAsync(true, ct);

    private async Task<Result<ReleaseVersion>> GetLatestVersionAsync(bool includePreReleases, CancellationToken ct)
    {
        try
        {
            var response = await _httpClient.GetAsync("releases?per_page=100", ct);
            response.EnsureSuccessStatusCode();
            string json = await response.Content.ReadAsStringAsync(ct);
            var jsonRoot = JsonDocument.Parse(json).RootElement;

            ReleaseVersion? latest = null;
            foreach (var element in jsonRoot.EnumerateArray())
            {
                if (!IsCandidate(element, includePreReleases))
                {
                    continue;
                }

                var versionResult = ReleaseVersion.Parse(element);
                if (!versionResult.IsSuccess)
                {
                    Logger.LogWarning($"Skipping release '{element.GetProperty("tag_name")}': {versionResult.Error}");
                    continue;
                }

                if (latest == null || versionResult.Value > latest)
                {
                    latest = versionResult.Value;
                }
            }

            if (latest == null)
            {
                return includePreReleases ? "No release found" : "No full release found";
            }

            return latest;
        }
        catch (TaskCanceledException)
        {
            return string.Empty;
        }
        catch (Exception e)
        {
            Logger.LogError(e.ToString());
            return "An error occurred while fetching the latest version";
        }
    }

    public async Task<Result<ReleaseVersion>> GetVersionAsync(string version, CancellationToken ct)
    {
        string tag = ReleaseVersion.ToTagName(version.Trim());
        try
        {
            var response = await _httpClient.GetAsync($"releases/tags/{Uri.EscapeDataString(tag)}", ct);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return $"There is no release {tag}";
            }

            response.EnsureSuccessStatusCode();
            string json = await response.Content.ReadAsStringAsync(ct);
            var element = JsonDocument.Parse(json).RootElement;
            if (element.TryGetProperty("draft", out var draft) && draft.GetBoolean())
            {
                return $"There is no published release {tag}";
            }

            return ReleaseVersion.Parse(element);
        }
        catch (TaskCanceledException)
        {
            return string.Empty;
        }
        catch (Exception e)
        {
            Logger.LogError(e.ToString());
            return $"An error occurred while fetching the release {tag}";
        }
    }

    public async Task<Result<FileInfo>> DownloadAssetAsync(string url, string targetDirectory, CancellationToken ct)
    {
        try
        {
            using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();

            Directory.CreateDirectory(targetDirectory);
            string fileName = Path.GetFileName(new Uri(url).LocalPath);
            string targetPath = Path.Combine(targetDirectory, fileName);

            await using var source = await response.Content.ReadAsStreamAsync(ct);
            await using var target = File.Create(targetPath);
            await source.CopyToAsync(target, ct);

            return new FileInfo(targetPath);
        }
        catch (TaskCanceledException)
        {
            return string.Empty;
        }
        catch (Exception e)
        {
            Logger.LogError(e.ToString());
            return "An error occurred while downloading the asset";
        }
    }

    private static bool IsCandidate(JsonElement release, bool includePreReleases)
    {
        if (release.TryGetProperty("draft", out var draft) && draft.GetBoolean())
        {
            return false;
        }

        return includePreReleases || !(release.TryGetProperty("prerelease", out var preRelease) && preRelease.GetBoolean());
    }
}