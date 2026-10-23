using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace CoDTweaker.Services;

public sealed record ReleaseInfo(string Tag, Version Version, string Url);

/// <summary>Asks GitHub for the latest release. Any failure (offline, private repo, rate limit) just means "no answer".</summary>
public static class UpdateCheck
{
    public const string Repo = "SpockLSM/cod-tweaker";

    public static Version Current
    {
        get
        {
            string v = CrashLog.Version;
            int plus = v.IndexOf('+');
            if (plus >= 0) v = v[..plus];
            return Version.TryParse(v, out var parsed) ? Normalize(parsed) : new Version(0, 0, 0);
        }
    }

    public static async Task<ReleaseInfo?> LatestAsync(CancellationToken ct = default)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("CoDTweaker", Current.ToString()));
            http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            using var doc = JsonDocument.Parse(await http.GetStringAsync($"https://api.github.com/repos/{Repo}/releases/latest", ct));
            string tag = doc.RootElement.GetProperty("tag_name").GetString() ?? "";
            string url = doc.RootElement.GetProperty("html_url").GetString() ?? $"https://github.com/{Repo}/releases/latest";
            return Version.TryParse(tag.TrimStart('v', 'V'), out var v) ? new ReleaseInfo(tag, Normalize(v), url) : null;
        }
        catch { return null; }
    }

    /// <summary>True when the release is newer than the running app.</summary>
    public static bool IsNewer(ReleaseInfo release) => release.Version > Current;

    static Version Normalize(Version v) => new(v.Major, Math.Max(0, v.Minor), Math.Max(0, v.Build));
}
