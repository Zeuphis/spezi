using System.Security.Cryptography;
using Spezi.Core;
using Xunit;

namespace Spezi.Tests;

public class UpdateCheckerTests
{
    [Theory]
    [InlineData("v1.1.3", "1.1.2", true)]
    [InlineData("1.2.0", "1.1.9", true)]
    [InlineData("v2.0", "1.9.9", true)]
    [InlineData("v1.1.2", "1.1.2", false)]
    [InlineData("v1.1.1", "1.1.2", false)]
    [InlineData("v1.10.0", "1.9.0", true)]
    [InlineData("v1.1.3-beta+7", "1.1.2", true)]
    [InlineData("nonsense", "1.1.2", false)]
    [InlineData("", "1.1.2", false)]
    public void ComparesSemanticVersions(string tag, string current, bool newer) =>
        Assert.Equal(newer, UpdateChecker.IsNewer(tag, Version.Parse(current)));

    [Fact]
    public void FourPartAssemblyVersionIsNormalized() =>
        Assert.False(UpdateChecker.IsNewer("v1.1.2", new Version(1, 1, 2, 0)));

    [Fact]
    public void VerifiesSha256()
    {
        var file = Path.GetTempFileName();
        try
        {
            File.WriteAllText(file, "spezi");
            var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file)));
            Assert.True(UpdateChecker.VerifySha256(file, hash));
            Assert.True(UpdateChecker.VerifySha256(file, hash.ToLowerInvariant() + " *Spezi-Updater.exe\r\n"));
            Assert.False(UpdateChecker.VerifySha256(file, new string('0', 64)));
            Assert.False(UpdateChecker.VerifySha256(file, "not a hash"));
            Assert.False(UpdateChecker.VerifySha256(file, null));
        }
        finally { File.Delete(file); }
    }

    const string Release = """
    {"tag_name":"v1.2.0","html_url":"https://github.com/Zeuphis/spezi/releases/tag/v1.2.0","draft":false,"prerelease":false,
     "assets":[{"name":"Spezi-Setup.exe","browser_download_url":"https://example.com/Spezi-Setup.exe"},
               {"name":"Spezi-Setup.exe.sha256","browser_download_url":"https://example.com/Spezi-Setup.exe.sha256"},
               {"name":"Spezi-Updater.exe","browser_download_url":"https://example.com/Spezi-Updater.exe"},
               {"name":"Spezi-Updater.exe.sha256","browser_download_url":"https://example.com/Spezi-Updater.exe.sha256"}]}
    """;

    [Fact]
    public void ParsesReleaseAndPrefersUpdater()
    {
        var update = UpdateChecker.ParseRelease(Release, new Version(1, 1, 2));
        Assert.NotNull(update);
        Assert.Equal(new Version(1, 2, 0), update!.Version);
        Assert.Equal("Spezi-Updater.exe", update.Package.Name);
        Assert.Equal("Spezi-Updater.exe.sha256", update.Checksum.Name);
    }

    [Fact]
    public void IgnoresOlderUnverifiableOrInsecureReleases()
    {
        Assert.Null(UpdateChecker.ParseRelease(Release, new Version(1, 2, 0)));
        Assert.Null(UpdateChecker.ParseRelease("""{"tag_name":"v9.0.0","assets":[{"name":"Spezi-Updater.exe","browser_download_url":"https://example.com/u.exe"}]}""", new Version(1, 0, 0)));
        Assert.Null(UpdateChecker.ParseRelease("""{"tag_name":"v9.0.0","assets":[{"name":"Spezi-Updater.exe","browser_download_url":"http://example.com/u.exe"},{"name":"Spezi-Updater.exe.sha256","browser_download_url":"http://example.com/u.sha256"}]}""", new Version(1, 0, 0)));
        Assert.Null(UpdateChecker.ParseRelease("""{"tag_name":"v9.0.0","prerelease":true,"assets":[]}""", new Version(1, 0, 0)));
    }

    [Fact]
    public void ApiUrlUsesConfiguredRepository() =>
        Assert.Equal("https://api.github.com/repos/Zeuphis/spezi/releases/latest", UpdateChecker.LatestReleaseApi.ToString());
}
