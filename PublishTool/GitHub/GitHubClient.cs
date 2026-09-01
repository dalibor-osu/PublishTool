using System.Net;
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

    public async Task<Result<ReleaseVersion>> GetLatestFullVersionAsync(CancellationToken ct)
    {
        try
        {
            var response = await _httpClient.GetAsync("releases/latest", ct);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return "No full release found";
            }
            response.EnsureSuccessStatusCode();
            string json = await response.Content.ReadAsStringAsync(ct);
            var jsonRoot = JsonDocument.Parse(json).RootElement;
            return ReleaseVersion.Parse(jsonRoot);
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

    public async Task<Result<ReleaseVersion>> GetLatestVersionIncludingPreReleaseAsync(CancellationToken ct)
    {
        try
        {
            var response = await _httpClient.GetAsync("releases", ct);
            response.EnsureSuccessStatusCode();
            string json = await response.Content.ReadAsStringAsync(ct);
            var jsonRoot = JsonDocument.Parse(json).RootElement;
            var versions = new List<ReleaseVersion>(jsonRoot.GetArrayLength());
            foreach (var element in jsonRoot.EnumerateArray())
            {
                var currentVersionResult = ReleaseVersion.Parse(element);
                if (!currentVersionResult.IsSuccess)
                {
                    return currentVersionResult.Error;
                }

                versions.Add(currentVersionResult.Value);
            }

            return versions.OrderByDescending(v => v).First();
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
}