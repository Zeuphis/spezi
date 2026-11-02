using System.Text.RegularExpressions;

namespace Spezi.Core.Recording;

public static class ClipLibrary
{
    static readonly Regex LegacyName = new(@"^(?<game>.+)_(?:Clip|Aufnahme|Recording|Session|Wiederhergestellt|Recovered)_(?:\d{4}-\d{2}-\d{2}_\d{2}-\d{2}-\d{2}(?:-\d{3})?(?:_[0-9a-f]{12,32})?|\d{8}_\d{6}(?:_\d{3})?(?:_[0-9a-f]{12,32})?)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static string GameDirectory(string root, string? gameTitle)
    {
        var game = string.IsNullOrWhiteSpace(gameTitle) || gameTitle.Equals("Spezi", StringComparison.OrdinalIgnoreCase)
            ? "General" : RecordingNames.Clean(gameTitle);
        // Reuse an existing fallback folder from older releases, without moving user clips.
        if (game == "General" && !Directory.Exists(Path.Combine(root, game)) && Directory.Exists(Path.Combine(root, "Allgemein")))
            game = "Allgemein";
        var directory = Path.Combine(Path.GetFullPath(root), game);
        if (Directory.Exists(directory) && (File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("The game folder cannot be a link to another location.");
        return directory;
    }

    public static IEnumerable<string> VideoFiles(string root)
    {
        if (!Directory.Exists(root)) yield break;
        var directories = new Stack<string>();
        directories.Push(root);
        var options = new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
        while (directories.TryPop(out var directory))
        {
            string[] files;
            string[] children;
            try
            {
                files = Directory.GetFiles(directory, "*.mp4", options);
                children = Directory.GetDirectories(directory, "*", options);
            }
            catch (IOException) { continue; }
            catch (UnauthorizedAccessException) { continue; }
            foreach (var file in files) yield return file;
            foreach (var child in children)
                if (!Path.GetFileName(child).StartsWith('.')) directories.Push(child);
        }
    }

    public static int OrganizeLegacyFiles(string root)
    {
        if (!Directory.Exists(root)) return 0;
        string[] files;
        try { files = Directory.GetFiles(root, "*.mp4", new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint }); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { AppLog.Error("Could not read existing clips.", ex); return 0; }
        var moved = 0;
        foreach (var file in files)
        {
            var match = LegacyName.Match(Path.GetFileNameWithoutExtension(file));
            if (!match.Success) continue;
            try
            {
                var directory = GameDirectory(root, match.Groups["game"].Value);
                var destination = Path.Combine(directory, Path.GetFileName(file));
                // A name collision never replaces either video. Both remain visible in the gallery.
                if (File.Exists(destination)) continue;
                Directory.CreateDirectory(directory);
                File.Move(file, destination);
                moved++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { AppLog.Error("Could not organize clip: " + file, ex); }
        }
        return moved;
    }
}
