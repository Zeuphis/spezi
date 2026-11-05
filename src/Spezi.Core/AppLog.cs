using System.Text;

namespace Spezi.Core;

public static class AppLog
{
    static readonly object Gate = new();
    // SPEZI_LOG_DIR redirects the log (tests use it so they never write to the user's log).
    public static string FilePath { get; } = Path.Combine(
        Environment.GetEnvironmentVariable("SPEZI_LOG_DIR") is { Length: > 0 } dir
            ? dir
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Spezi"),
        "spezi.log");

    public static void Info(string message) => Write("INFO", message);

    public static void Error(string message, Exception? exception = null)
    {
        var text = exception == null ? message : message + " | " + exception;
        Write("ERROR", text);
    }

    const long MaxBytes = 4 * 1024 * 1024;
    static string? _lastMessage;
    static long _lastAt;
    static int _repeats;

    static void Write(string level, string message)
    {
        try
        {
            lock (Gate)
            {
                // A failure repeating on a hot path (e.g. 50x per second on the audio thread) must not
                // grow the log without bound or slow the caller: collapse repeats within 10 s.
                var now = Environment.TickCount64;
                if (message == _lastMessage && now - _lastAt < 10_000) { _repeats++; return; }
                var prefix = _repeats > 0 ? $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} INFO (previous message repeated {_repeats} more times){Environment.NewLine}" : "";
                _lastMessage = message; _lastAt = now; _repeats = 0;
                var line = prefix + $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {level} {message}{Environment.NewLine}";
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                var info = new FileInfo(FilePath);
                if (info.Exists && info.Length > MaxBytes)
                    File.Move(FilePath, Path.ChangeExtension(FilePath, ".old.log"), true);
                File.AppendAllText(FilePath, line, Encoding.UTF8);
            }
        }
        catch
        {
            // Logging must not break recording.
        }
    }
}
