using System.IO;
using System.Text.Json;

using OpenTrackpad.Core.Haptics;

namespace OpenTrackpad.App;

public sealed class AppSettings
{
    public bool DragEnabled { get; set; }
    public double? DragGain { get; set; }
    public bool TrackingSpeedSeeded { get; set; }
    public bool TrayHintShown { get; set; }
    public string? Language { get; set; }
    public int? OriginalPanSensitivity { get; set; }
    public int? OriginalScrollDirection { get; set; }
    public int? OriginalThreeFingerSlide { get; set; }
    public int? OriginalFourFingerSlide { get; set; }
    public HapticSettings? QuietClickBackup { get; set; }
}

public static class AppPaths
{
    public static string Root { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "OpenTrackpad");

    public static string SettingsFile => Path.Combine(Root, "settings.json");
}

public static class AppSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(AppPaths.SettingsFile))
                return new AppSettings();

            string json = File.ReadAllText(AppPaths.SettingsFile);
            return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public static void Save(AppSettings settings)
    {
        Directory.CreateDirectory(AppPaths.Root);
        File.WriteAllText(AppPaths.SettingsFile, JsonSerializer.Serialize(settings, JsonOptions));
    }
}
