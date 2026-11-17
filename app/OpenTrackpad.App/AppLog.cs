using System.IO;

namespace OpenTrackpad.App;

public static class AppLog
{
    private const long MaxBytes = 2 * 1024 * 1024;
    private const int MaxBackups = 3;

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
                RotateIfNeeded();
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

    private static void RotateIfNeeded()
    {
        var file = new FileInfo(FilePath);
        if (!file.Exists || file.Length < MaxBytes) return;

        for (int index = MaxBackups; index >= 1; index--)
        {
            string source = index == 1 ? FilePath : $"{FilePath}.{index - 1}";
            string target = $"{FilePath}.{index}";

            if (File.Exists(target)) File.Delete(target);
            if (File.Exists(source)) File.Move(source, target);
        }
    }
}
