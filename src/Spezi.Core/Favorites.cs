using System.Text.Json;

namespace Spezi.Core;

// Favorite clips, keyed by path relative to the clip folder. Stored beside the log in
// %LocalAppData%\Spezi\favorites.json; video files are never touched.
public static class Favorites
{
    static readonly object Gate = new();
    static HashSet<string>? _keys;

    public static string FilePath { get; } = Path.Combine(Path.GetDirectoryName(AppLog.FilePath)!, "favorites.json");

    public static string Key(string root, string path) =>
        Path.GetRelativePath(Path.GetFullPath(root), Path.GetFullPath(path)).Replace('\\', '/');

    static HashSet<string> Keys()
    {
        if (_keys != null) return _keys;
        _keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (File.Exists(FilePath) && JsonSerializer.Deserialize<string[]>(File.ReadAllText(FilePath)) is { } stored)
                _keys.UnionWith(stored);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            AppLog.Error("Favorites could not be read.", ex);
        }
        return _keys;
    }

    public static bool Contains(string root, string path)
    {
        lock (Gate) return Keys().Contains(Key(root, path));
    }

    public static void Set(string root, string path, bool favorite)
    {
        lock (Gate)
        {
            var changed = favorite ? Keys().Add(Key(root, path)) : Keys().Remove(Key(root, path));
            if (changed) Save();
        }
    }

    public static void Move(string root, string from, string to)
    {
        lock (Gate)
        {
            if (Keys().Remove(Key(root, from))) { Keys().Add(Key(root, to)); Save(); }
        }
    }

    // Drops entries whose file no longer exists (deleted or renamed outside Spezi).
    public static void Prune(string root)
    {
        if (!Directory.Exists(root)) return;
        lock (Gate)
        {
            var removed = Keys().RemoveWhere(key => !File.Exists(Path.Combine(root, key)));
            if (removed > 0) Save();
        }
    }

    static void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            var temp = FilePath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(_keys!.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToArray()));
            File.Move(temp, FilePath, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Error("Favorites could not be saved.", ex);
        }
    }
}
