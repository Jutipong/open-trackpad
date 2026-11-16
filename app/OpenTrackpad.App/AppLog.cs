using System.IO;

namespace OpenTrackpad.App;

public static class AppLog
{
    private static readonly object Gate = new();

    public static string FilePath => Path.Combine(AppPaths.Root, "app.log");

    public static void Info(string message) => Write("INFO", message);

    public static void Error(string message, Exception? exception = null)
    {
        string text = exception is null ? message : $"{message}\n{exception}";
        Write("ERROR", text);
    }

    private static void Write(string level, string message)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.Root);
            lock (Gate)
            {
                File.AppendAllText(
                    FilePath,
                    $"[{DateTimeOffset.Now:O}] [{level}] {message}\n");
            }
        }
        catch
        {
            // Logging must never break the app.
        }
    }
}
