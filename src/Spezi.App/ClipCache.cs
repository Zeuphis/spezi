using System.Security.Cryptography;
using System.Text;
using System.Windows.Media.Imaging;
using System.Windows.Media;
using Spezi.Core;
using Spezi.Core.Recording;

namespace Spezi.App;

static class ClipCache
{
    const int PreviewWidth = 320;
    const int MemoryLimit = 64;
    readonly static SemaphoreSlim DecodeGate = new(1, 1);
    readonly static object CacheGate = new();
    readonly static Dictionary<string, (ClipDetails Details, LinkedListNode<string> Node)> Memory = new(StringComparer.Ordinal);
    readonly static LinkedList<string> Order = new();
    internal sealed record ClipDetails(TimeSpan Duration, BitmapSource? Thumbnail);
    public static string Directory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Spezi", "thumbs");

    public static string FileFor(string videoPath)
    {
        var file = new FileInfo(videoPath);
        var identity = $"opaque-v2|{file.FullName}|{file.Length}|{file.LastWriteTimeUtc.Ticks}";
        return Path.Combine(Directory, Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(identity))) + ".png");
    }

    static BitmapSource Load(string cachePath)
    {
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.DecodePixelWidth = PreviewWidth;
        image.UriSource = new Uri(cachePath);
        image.EndInit();
        image.Freeze();
        return Raster(image);
    }

    static BitmapSource Raster(BitmapSource source)
    {
        if (source.Format != PixelFormats.Bgra32)
            source = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        var stride = checked(source.PixelWidth * 4);
        var pixels = new byte[checked(stride * source.PixelHeight)];
        source.CopyPixels(pixels, stride, 0);
        var result = BitmapSource.Create(source.PixelWidth, source.PixelHeight, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
        result.Freeze();
        return result;
    }

    static bool TryCached(string key, out ClipDetails details)
    {
        lock (CacheGate)
        {
            if (!Memory.TryGetValue(key, out var entry)) { details = null!; return false; }
            Order.Remove(entry.Node);
            Order.AddFirst(entry.Node);
            details = entry.Details;
            return true;
        }
    }

    public static Task<ClipDetails> GetDetailsAsync(string path, CancellationToken cancellation = default) => Task.Run(async () =>
    {
        cancellation.ThrowIfCancellationRequested();
        var cache = FileFor(path);
        if (TryCached(cache, out var cached)) return cached;
        await DecodeGate.WaitAsync(cancellation).ConfigureAwait(false);
        try
        {
            cancellation.ThrowIfCancellationRequested();
            if (TryCached(cache, out cached)) return cached;
            var details = ReadDetails(path, cache);
            lock (CacheGate)
            {
                Memory.Add(cache, (details, Order.AddFirst(cache)));
                while (Memory.Count > MemoryLimit)
                {
                    Memory.Remove(Order.Last!.Value);
                    Order.RemoveLast();
                }
            }
            return details;
        }
        finally { DecodeGate.Release(); }
    }, cancellation);

    public static async Task<BitmapSource?> GetThumbnailAsync(string path, CancellationToken cancellation = default) =>
        (await GetDetailsAsync(path, cancellation).ConfigureAwait(false)).Thumbnail;

    static ClipDetails ReadDetails(string path, string cache)
    {
        try { System.IO.Directory.CreateDirectory(Directory); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { AppLog.Error("Preview cache is unavailable.", ex); }
        var metadata = cache + ".duration";
        var duration = TimeSpan.Zero;
        try { if (File.Exists(metadata) && long.TryParse(File.ReadAllText(metadata), out var ticks) && ticks > 0) duration = TimeSpan.FromTicks(ticks); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* rebuild unavailable metadata */ }
        if (duration <= TimeSpan.Zero)
        {
            duration = MediaInspector.GetDuration(path);
            if (duration > TimeSpan.Zero)
            {
                try { File.WriteAllText(metadata, duration.Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture)); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { AppLog.Error("Preview duration could not be cached.", ex); }
            }
        }
        if (File.Exists(cache))
        {
            try { return new(duration, Load(cache)); }
            catch (Exception ex) { AppLog.Error("Thumbnail cache will be rebuilt: " + path, ex); }
        }
        if (!MediaInspector.TryReadThumbnail(path, out var pixels, out var width, out var height)) return new(duration, null);
        BitmapSource image = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
        image.Freeze();
        if (width > PreviewWidth)
        {
            var scale = PreviewWidth / (double)width;
            image = new TransformedBitmap(image, new ScaleTransform(scale, scale));
            image.Freeze();
        }
        // Detach the small raster from its full-resolution source and WIC decoder.
        image = Raster(image);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        var temporary = cache + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { using (var stream = File.Create(temporary)) encoder.Save(stream); File.Move(temporary, cache, true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { AppLog.Error("Preview image could not be cached.", ex); }
        finally { try { if (File.Exists(temporary)) File.Delete(temporary); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* incomplete cache file is private */ } }
        return new(duration, image);
    }
}

static class ClipText
{
    public static string FormatDuration(TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero)
            return "–";
        return duration.TotalHours >= 1 ? duration.ToString(@"h\:mm\:ss") : duration.ToString(@"mm\:ss");
    }
}
