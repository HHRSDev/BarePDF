using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace BarePDF.Updates;

public readonly record struct UpdateInfo(string Tag, string ReleaseUrl);

internal static class UpdateChecker
{
    private const string LatestReleaseUrl = "https://api.github.com/repos/HHRSDev/BarePDF/releases/latest";

    /// <summary>
    /// Returns non-null when a newer release exists on GitHub. Fails silently on
    /// network errors, timeouts, malformed JSON, etc. — no exception propagates.
    /// No identifiers sent beyond a generic User-Agent (required by the GitHub API).
    /// </summary>
    public static async Task<UpdateInfo?> CheckAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("BarePDF");
            http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");

            var json = await http.GetStringAsync(LatestReleaseUrl, cancellationToken);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var tagName = root.TryGetProperty("tag_name", out var tagProp) ? tagProp.GetString() ?? "" : "";
            var htmlUrl = root.TryGetProperty("html_url", out var urlProp) ? urlProp.GetString() ?? "" : "";

            if (string.IsNullOrWhiteSpace(tagName)) return null;

            if (!TryParseVersion(tagName, out var latest)) return null;

            var current = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0);
            if (Compare(latest, current) > 0)
            {
                return new UpdateInfo(tagName, htmlUrl);
            }
            return null;
        }
        catch
        {
            return null;
        }
    }

    private static bool TryParseVersion(string raw, out Version version)
    {
        var trimmed = raw.TrimStart('v', 'V').Split('-', '+')[0];
        return Version.TryParse(trimmed, out version!);
    }

    private static int Compare(Version a, Version b)
    {
        int Norm(int v) => Math.Max(0, v);
        var aN = new Version(a.Major, a.Minor, Norm(a.Build), Norm(a.Revision));
        var bN = new Version(b.Major, b.Minor, Norm(b.Build), Norm(b.Revision));
        return aN.CompareTo(bN);
    }
}
