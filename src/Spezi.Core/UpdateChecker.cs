using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Spezi.Core;

public sealed record ReleaseAsset(string Name, Uri Url);
public sealed record AvailableUpdate(Version Version, string Tag, Uri? Page, ReleaseAsset Package, ReleaseAsset Checksum);

// Startup update check against GitHub Releases. Anonymous only: no token is ever sent or stored.
public static partial class UpdateChecker
{
    public const string RepoOwner = "Zeuphis";
    public const string RepoName = "spezi";
    public static readonly string[] PackageNames = ["Spezi-Updater.exe", "Spezi-Setup.exe"];
    public static Uri LatestReleaseApi => new($"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases/latest");
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(8);

    // Accepts "v1.2.3", "1.2", "1.2.3-beta+5"; prerelease/build suffixes are ignored.
    public static bool TryParseVersion(string? text, out Version version)
    {
        version = new Version(0, 0, 0);
        if (string.IsNullOrWhiteSpace(text)) return false;
        var match = VersionPattern().Match(text.Trim());
        if (!match.Success) return false;
        int Part(int i) => match.Groups[i].Success ? int.Parse(match.Groups[i].Value) : 0;
        version = new Version(Part(1), Part(2), Part(3));
        return true;
    }

    public static Version Normalize(Version v) => new(v.Major, Math.Max(0, v.Minor), Math.Max(0, v.Build));
    public static bool IsNewer(string? tag, Version current) => TryParseVersion(tag, out var latest) && latest > Normalize(current);

    public static string? ParseSha256(string? text)
    {
        var match = text == null ? null : Sha256Pattern().Match(text);
        return match is { Success: true } ? match.Value.ToLowerInvariant() : null;
    }

    public static bool VerifySha256(string file, string? expected)
    {
        var hash = ParseSha256(expected);
        if (hash == null || !File.Exists(file)) return false;
        using var stream = File.OpenRead(file);
        var actual = Convert.ToHexString(SHA256.HashData(stream));
        return CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.ASCII.GetBytes(actual.ToLowerInvariant()), System.Text.Encoding.ASCII.GetBytes(hash));
    }

    public static AvailableUpdate? ParseRelease(string json, Version current)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.TryGetProperty("draft", out var draft) && draft.GetBoolean()) return null;
        if (root.TryGetProperty("prerelease", out var pre) && pre.GetBoolean()) return null;
        var tag = root.TryGetProperty("tag_name", out var t) ? t.GetString() : null;
        if (!IsNewer(tag, current) || !TryParseVersion(tag, out var version)) return null;
        var assets = new List<ReleaseAsset>();
        if (root.TryGetProperty("assets", out var list) && list.ValueKind == JsonValueKind.Array)
            foreach (var a in list.EnumerateArray())
                if (a.TryGetProperty("name", out var n) && a.TryGetProperty("browser_download_url", out var u)
                    && Uri.TryCreate(u.GetString(), UriKind.Absolute, out var url) && url.Scheme == Uri.UriSchemeHttps)
                    assets.Add(new(n.GetString() ?? "", url));
        foreach (var name in PackageNames)
        {
            var package = assets.FirstOrDefault(a => a.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            var sum = assets.FirstOrDefault(a => a.Name.Equals(name + ".sha256", StringComparison.OrdinalIgnoreCase));
            if (package != null && sum != null)
            {
                Uri? page = root.TryGetProperty("html_url", out var h) && Uri.TryCreate(h.GetString(), UriKind.Absolute, out var p) ? p : null;
                return new(version, tag!, page, package, sum);
            }
        }
        AppLog.Info($"Update {tag} has no package with a .sha256 checksum; ignored.");
        return null;
    }

    public static HttpClient CreateClient()
    {
        var http = new HttpClient { Timeout = Timeout };
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Spezi", Normalize(CurrentVersion).ToString()));
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return http;
    }

    public static Version CurrentVersion => typeof(UpdateChecker).Assembly.GetName().Version ?? new Version(0, 0, 0);

    // Never throws: network errors, 404 (private repo / no release) and timeouts are logged only.
    public static async Task<AvailableUpdate?> CheckAsync(HttpClient http, Version current, CancellationToken cancellation = default) =>
        (await CheckDetailedAsync(http, current, cancellation).ConfigureAwait(false)).Update;

    // Ok = false when GitHub could not be reached or answered with an error.
    public static async Task<(bool Ok, AvailableUpdate? Update)> CheckDetailedAsync(HttpClient http, Version current, CancellationToken cancellation = default)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            timeout.CancelAfter(Timeout);
            using var response = await http.GetAsync(LatestReleaseApi, timeout.Token).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                AppLog.Info("Update check: no public release found (404). The repository may be private.");
                return (false, null);
            }
            if (!response.IsSuccessStatusCode)
            {
                AppLog.Info($"Update check: GitHub returned {(int)response.StatusCode}.");
                return (false, null);
            }
            var json = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
            var update = ParseRelease(json, current);
            AppLog.Info(update == null ? "Update check: Spezi is up to date." : $"Update check: {update.Tag} is available.");
            return (true, update);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException or JsonException or InvalidOperationException)
        {
            AppLog.Info("Update check failed: " + ex.Message);
            return (false, null);
        }
    }

    // Downloads package and checksum over HTTPS; returns the verified file or throws.
    public static async Task<string> DownloadVerifiedAsync(HttpClient http, AvailableUpdate update, string directory, CancellationToken cancellation = default)
    {
        if (update.Package.Url.Scheme != Uri.UriSchemeHttps || update.Checksum.Url.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException("Update downloads must use HTTPS.");
        Directory.CreateDirectory(directory);
        var expected = ParseSha256(await http.GetStringAsync(update.Checksum.Url, cancellation).ConfigureAwait(false))
            ?? throw new InvalidOperationException("The update checksum file is invalid.");
        var path = Path.Combine(directory, Path.GetFileName(update.Package.Name));
        var partial = path + ".download";
        using (var download = new HttpClient { Timeout = TimeSpan.FromMinutes(10) })
        {
            download.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Spezi", Normalize(CurrentVersion).ToString()));
            await using var source = await download.GetStreamAsync(update.Package.Url, cancellation).ConfigureAwait(false);
            await using var target = File.Create(partial);
            await source.CopyToAsync(target, cancellation).ConfigureAwait(false);
        }
        if (!VerifySha256(partial, expected))
        {
            File.Delete(partial);
            throw new InvalidOperationException("The downloaded update failed the SHA-256 check and was deleted.");
        }
        File.Move(partial, path, true);
        return path;
    }

    [GeneratedRegex(@"^[vV]?(\d+)(?:\.(\d+))?(?:\.(\d+))?(?:\.\d+)?(?:[-+].*)?$")]
    private static partial Regex VersionPattern();
    [GeneratedRegex(@"\b[0-9a-fA-F]{64}\b")]
    private static partial Regex Sha256Pattern();
}
