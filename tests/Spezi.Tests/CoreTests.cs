using Spezi.Core;
using Spezi.Core.Recording;
using Xunit;

namespace Spezi.Tests;

// Hardware-free unit tests (run in CI). Encoder/audio regression tests stay in
// tests/Spezi.RecordingTests.csproj and need a real Windows PC with GPU/audio.
public sealed class LibraryTests : IDisposable
{
    readonly string _library = Path.Combine(Path.GetTempPath(), "SpeziUnit", Guid.NewGuid().ToString("N"));

    public LibraryTests() => Directory.CreateDirectory(_library);
    public void Dispose() { try { Directory.Delete(_library, true); } catch (IOException) { } }

    [Theory]
    [InlineData("Overwatch", "Overwatch")]
    [InlineData("Rainbow Six Siege", "Rainbow Six Siege")]
    [InlineData(null, "General")]
    [InlineData("CON", "_CON")]
    public void GameDirectoryNames(string? game, string expected) =>
        Assert.Equal(expected, Path.GetFileName(ClipLibrary.GameDirectory(_library, game)));

    [Fact]
    public void GameDirectoryStaysInsideLibrary()
    {
        var folder = ClipLibrary.GameDirectory(_library, "..\\outside/../A:B*");
        Assert.StartsWith(_library + Path.DirectorySeparatorChar, folder);
        Assert.DoesNotContain(Path.GetFileName(folder), c => Path.GetInvalidFileNameChars().Contains(c));
    }

    [Fact]
    public void LegacyFilesAreOrganizedLosslessly()
    {
        var name = RecordingNames.Create("Clip", "Overwatch");
        var old = Path.Combine(_library, name);
        File.WriteAllText(old, "original video bytes");
        var unrelated = Path.Combine(_library, "my-other-video.mp4");
        File.WriteAllText(unrelated, "user video");
        Assert.Equal(1, ClipLibrary.OrganizeLegacyFiles(_library));
        var organized = Path.Combine(_library, "Overwatch", name);
        Assert.False(File.Exists(old));
        Assert.Equal("original video bytes", File.ReadAllText(organized));
        Assert.True(File.Exists(unrelated));
        Assert.Equal(0, ClipLibrary.OrganizeLegacyFiles(_library));
        File.WriteAllText(old, "different original video");
        Assert.Equal(0, ClipLibrary.OrganizeLegacyFiles(_library));
        Assert.Equal("original video bytes", File.ReadAllText(organized));
        var pending = Path.Combine(_library, "Overwatch", ".spezi-pending");
        Directory.CreateDirectory(pending);
        File.WriteAllText(Path.Combine(pending, "partial.mp4"), "unfinished");
        var visible = ClipLibrary.VideoFiles(_library).ToArray();
        Assert.Equal(3, visible.Length);
        Assert.All(visible, p => Assert.DoesNotContain(".spezi-pending", p));
    }
}

public class SettingsTests
{
    [Fact]
    public void DefaultsAndClamp()
    {
        var s = System.Text.Json.JsonSerializer.Deserialize<AppSettings>("{}")!;
        Assert.Equal(3.5, s.ToastDurationSeconds);
        Assert.Equal(AppSettings.DefaultAccentColor, s.AccentColor);
        s.ToastDurationSeconds = double.NaN; s.AccentColor = "invalid"; s.Clamp();
        Assert.Equal(3.5, s.ToastDurationSeconds);
        Assert.Equal(AppSettings.DefaultAccentColor, s.AccentColor);
        s.ToastDurationSeconds = 99; s.AccentColor = "#ac8bfa"; s.Clamp();
        Assert.Equal(15, s.ToastDurationSeconds);
        Assert.Equal("#AC8BFA", s.AccentColor);
    }
}

public class NamingTests
{
    [Theory]
    [InlineData("Overwatch", "", "Overwatch")]
    [InlineData("RainbowSix_DX11", "", "Rainbow Six Siege")]
    [InlineData("chrome", "YouTube", null)]
    [InlineData("NewGame", "A New Game", "A New Game")]
    public void GameTitleDetection(string process, string title, string? expected) =>
        Assert.Equal(expected, GameTitles.Resolve(process, title, true));

    [Fact]
    public void RecordingNamesAreSafe()
    {
        Assert.StartsWith("Rainbow Six Siege_Clip_", RecordingNames.Create("Clip", "Rainbow Six Siege"));
        Assert.DoesNotContain(RecordingNames.Create("Recording", "A:B/C?D*"), c => Path.GetInvalidFileNameChars().Contains(c));
    }

    [Fact]
    public void ReplayMemoryEstimateMatchesBufferLimit()
    {
        // 5 min at 15 Mbit/s: 562.5 MB * 1.35 + 32 MB ~= 756 MB
        var bytes = ReplayBuffer.MemoryLimitBytes(TimeSpan.FromMinutes(5), 15000);
        Assert.InRange(bytes / (1024 * 1024), 700, 800);
        Assert.Equal(64L * 1024 * 1024, ReplayBuffer.MemoryLimitBytes(TimeSpan.FromSeconds(15), 2000));
        Assert.Contains("MB of RAM", AppSettings.ReplayMemoryText(300, 15000));
    }

    [Fact]
    public void QpcConversion()
    {
        var longUptime = 4L * 24 * 3600 * 10_000_000;
        Assert.Equal(longUptime, MediaClock.CounterToTicks(longUptime, 10_000_000));
        Assert.Equal(61728394506172, MediaClock.CounterToTicks(123456789012345, 20_000_000));
    }
}
