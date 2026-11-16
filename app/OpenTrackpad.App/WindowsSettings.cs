using System.IO;
using System.Security;
using Microsoft.Win32;

namespace OpenTrackpad.App;

public sealed record DriverSettings(
    uint FeedbackClick,
    uint FeedbackRelease,
    uint StopPressure,
    uint StopSize,
    bool IgnoreNearFingers,
    bool IgnoreButtonFinger,
    bool PalmRejection,
    bool InvertMovement,
    bool ButtonDisabled)
{
    public const uint DefaultFeedbackClick = 0x08081E;
    public const uint DefaultFeedbackRelease = 0x020218;
    public const uint DefaultStopPressure = 0xFFFFFFFF;
    public const uint DefaultStopSize = 0xFFFFFFFF;

    public static DriverSettings Default { get; } = new(
        DefaultFeedbackClick,
        DefaultFeedbackRelease,
        DefaultStopPressure,
        DefaultStopSize,
        IgnoreNearFingers: true,
        IgnoreButtonFinger: false,
        PalmRejection: false,
        InvertMovement: false,
        ButtonDisabled: false);
}

public sealed class AdministratorRequiredException : Exception
{
    public AdministratorRequiredException(string message) : base(message) { }
    public AdministratorRequiredException(string message, Exception inner) : base(message, inner) { }
}

public static class DriverSettingsStore
{
    public const string RegistryKey =
        @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\WUDF\Services\AmtPtpDeviceUsbUm\Parameters";

    public static DriverSettings Read()
    {
        using var key = Registry.LocalMachine.OpenSubKey(RegistryKey, writable: false);
        if (key is null)
            return DriverSettings.Default;

        return new DriverSettings(
            ReadDword(key, "FeedbackClick", DriverSettings.DefaultFeedbackClick),
            ReadDword(key, "FeedbackRelease", DriverSettings.DefaultFeedbackRelease),
            ReadDword(key, "StopPressure", DriverSettings.DefaultStopPressure),
            ReadDword(key, "StopSize", DriverSettings.DefaultStopSize),
            ReadDword(key, "IgnoreNearFingers", 1) != 0,
            ReadDword(key, "IgnoreButtonFinger", 0) != 0,
            ReadDword(key, "PalmRejection", 0) != 0,
            ReadDword(key, "InvertMovement", 0) != 0,
            ReadDword(key, "ButtonDisabled", 0) != 0);
    }

    public static void Write(DriverSettings settings)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(RegistryKey, writable: true)
                ?? throw new InvalidOperationException("Driver settings key not found.");

            key.SetValue("FeedbackClick", unchecked((int)settings.FeedbackClick), RegistryValueKind.DWord);
            key.SetValue("FeedbackRelease", unchecked((int)settings.FeedbackRelease), RegistryValueKind.DWord);
            key.SetValue("StopPressure", unchecked((int)settings.StopPressure), RegistryValueKind.DWord);
            key.SetValue("StopSize", unchecked((int)settings.StopSize), RegistryValueKind.DWord);
            key.SetValue("IgnoreNearFingers", settings.IgnoreNearFingers ? 1 : 0, RegistryValueKind.DWord);
            key.SetValue("IgnoreButtonFinger", settings.IgnoreButtonFinger ? 1 : 0, RegistryValueKind.DWord);
            key.SetValue("PalmRejection", settings.PalmRejection ? 1 : 0, RegistryValueKind.DWord);
            key.SetValue("InvertMovement", settings.InvertMovement ? 1 : 0, RegistryValueKind.DWord);
            key.SetValue("ButtonDisabled", settings.ButtonDisabled ? 1 : 0, RegistryValueKind.DWord);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or SecurityException)
        {
            throw new AdministratorRequiredException(
                "ต้องรัน OpenTrackpad แบบ administrator เพื่อแก้ไขค่าของไดรเวอร์", ex);
        }
    }

    private static uint ReadDword(RegistryKey key, string name, uint fallback)
    {
        object? value = key.GetValue(name);
        return value switch
        {
            int i => unchecked((uint)i),
            uint u => u,
            long l => unchecked((uint)l),
            _ => fallback,
        };
    }
}

public static class ScrollSettingsStore
{
    public const string RegistryKey = @"Software\Microsoft\Windows\CurrentVersion\PrecisionTouchPad";

    public static int ReadPanSensitivity()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RegistryKey, writable: false);
        return key?.GetValue("PanSensitivity") is int value ? Math.Clamp(value, 0, 100) : 50;
    }

    public static void WritePanSensitivity(int value)
    {
        if (value is < 0 or > 100)
            throw new ArgumentOutOfRangeException(nameof(value), "PanSensitivity is 0..100.");

        using var key = Registry.CurrentUser.CreateSubKey(RegistryKey, writable: true)
            ?? throw new InvalidOperationException("Touchpad settings key not found.");
        key.SetValue("PanSensitivity", value, RegistryValueKind.DWord);
    }
}

public static class StartupManager
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "OpenTrackpad";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
        return key?.GetValue(ValueName) is string;
    }

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true)
            ?? throw new InvalidOperationException("Run key not found.");

        if (enabled)
            key.SetValue(ValueName, BuildLaunchCommand(), RegistryValueKind.String);
        else
            key.DeleteValue(ValueName, throwOnMissingValue: false);
    }

    private static string BuildLaunchCommand()
    {
        string? exe = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(exe))
            throw new InvalidOperationException("Cannot determine the application path.");

        if (Path.GetFileName(exe).Equals("dotnet.exe", StringComparison.OrdinalIgnoreCase))
        {
            string? dll = System.Reflection.Assembly.GetEntryAssembly()?.Location;
            if (!string.IsNullOrWhiteSpace(dll))
                return $"\"{exe}\" \"{dll}\"";
        }

        return $"\"{exe}\"";
    }
}
