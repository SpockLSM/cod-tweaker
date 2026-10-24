using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace Spock.AppKit;

public sealed record ReleaseInfo(string Tag, Version Version, string Url);

/// <summary>Asks GitHub for a repository's latest release. Any failure (offline, private repo, rate limit) just means "no answer".</summary>
public sealed class UpdateCheck
{
    public string Repo { get; }
    public string UserAgent { get; }

    /// <param name="repo">"owner/name" on GitHub.</param>
    /// <param name="userAgent">GitHub requires one; the app's name is fine.</param>
    public UpdateCheck(string repo, string userAgent)
    {
        Repo = repo;
        UserAgent = userAgent;
    }

    public async Task<ReleaseInfo?> LatestAsync(CancellationToken ct = default)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue(UserAgent.Replace(' ', '-'), AppVersion.Short));
            http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            string json = await http.GetStringAsync($"https://api.github.com/repos/{Repo}/releases/latest", ct);
            return Parse(json);
        }
        catch { return null; }
    }

    /// <summary>The tag and page of a release from GitHub's JSON; null when it isn't a version tag.</summary>
    public ReleaseInfo? Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        string tag = doc.RootElement.TryGetProperty("tag_name", out var t) ? t.GetString() ?? "" : "";
        string url = doc.RootElement.TryGetProperty("html_url", out var u) ? u.GetString() ?? "" : "";
        if (url.Length == 0) url = $"https://github.com/{Repo}/releases/latest";
        var v = AppVersion.Parse(tag);
        return v == null ? null : new ReleaseInfo(tag, v, url);
    }

    /// <summary>True when the release is newer than the running app.</summary>
    public static bool IsNewer(ReleaseInfo release) => IsNewer(release, AppVersion.Current);
    public static bool IsNewer(ReleaseInfo release, Version current) => release.Version > current;
}
