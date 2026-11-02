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

    static void Write(string level, string message)
    {
        try
        {
            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {level} {message}{Environment.NewLine}";
            lock (Gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                File.AppendAllText(FilePath, line, Encoding.UTF8);
            }
        }
        catch
        {
            // Logging must not break recording.
        }
    }
}
