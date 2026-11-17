using System.IO;
using System.Security;
using Microsoft.Win32;
using OpenTrackpad.Interop.Driver;
using OpenTrackpad.Interop.RawInput;

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
    public const uint DefaultStopPressure = 0;
    public const uint DefaultStopSize = 7;

    public static DriverSettings Default { get; } = new(
        DefaultFeedbackClick,
        DefaultFeedbackRelease,
        DefaultStopPressure,
        DefaultStopSize,
        IgnoreNearFingers: true,
        IgnoreButtonFinger: false,
        PalmRejection: true,
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
    public const string UsbRegistryKey =
        @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\WUDF\Services\AmtPtpDeviceUsbUm\Parameters";

    public const string BluetoothRegistryKey =
        @"SYSTEM\CurrentControlSet\Services\AmtPtpHidFilter\Parameters";

    public static DriverSettings Read()
    {
        string? preferredKey = null;
        try
        {
            TrackpadTransport transport = TrackpadDeviceLocator.FindPreferred()?.Transport
                ?? TrackpadTransport.None;
            if (transport == TrackpadTransport.Usb)
                preferredKey = UsbRegistryKey;
            else if (transport == TrackpadTransport.Bluetooth)
                preferredKey = BluetoothRegistryKey;
        }
        catch
        {
            // Fall back to whichever key is readable.
        }

        using RegistryKey? key =
            (preferredKey is null ? null : Registry.LocalMachine.OpenSubKey(preferredKey, writable: false))
            ?? Registry.LocalMachine.OpenSubKey(UsbRegistryKey, writable: false)
            ?? Registry.LocalMachine.OpenSubKey(BluetoothRegistryKey, writable: false);

        if (key is null)
            return DriverSettings.Default;

        return new DriverSettings(
            ReadDword(key, "FeedbackClick", DriverSettings.DefaultFeedbackClick),
            ReadDword(key, "FeedbackRelease", DriverSettings.DefaultFeedbackRelease),
            ReadDword(key, "StopPressure", DriverSettings.DefaultStopPressure),
            ReadDword(key, "StopSize", DriverSettings.DefaultStopSize),
            ReadDword(key, "IgnoreNearFingers", 1) != 0,
            ReadDword(key, "IgnoreButtonFinger", 0) != 0,
            ReadDword(key, "PalmRejection", 1) != 0,
            ReadDword(key, "InvertMovement", 0) != 0,
            ReadDword(key, "ButtonDisabled", 0) != 0);
    }

    public static void Write(DriverSettings settings)
    {
        try
        {
            // Keep both transports in sync so switching between USB and
            // Bluetooth keeps the same click feel and pointer settings.
            Write(UsbRegistryKey, settings);
            Write(BluetoothRegistryKey, settings);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or SecurityException)
        {
            throw new AdministratorRequiredException(
                Loc.T("Driver.NeedAdmin"), ex);
        }
    }

    private static void Write(string registryKey, DriverSettings settings)
    {
        using RegistryKey key = Registry.LocalMachine.CreateSubKey(registryKey, writable: true)
            ?? throw new InvalidOperationException($"Driver settings key not found: {registryKey}");

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

public enum TouchpadGesturePreset
{
    Nothing = 0,
    SwitchApps = 1,
    SwitchDesktops = 2,
    ChangeAudio = 3,
}

/// <summary>
/// Windows three- and four-finger swipe presets. Values are documented Windows
/// settings; 65535 means per-direction custom gestures and is left untouched.
/// Windows may cache these values until sign-out or Explorer restart.
/// </summary>
public static class GestureSettingsStore
{
    public const string RegistryKey = @"Software\Microsoft\Windows\CurrentVersion\PrecisionTouchPad";
    public const string ThreeFingerSlide = "ThreeFingerSlideEnabled";
    public const string FourFingerSlide = "FourFingerSlideEnabled";

    public static TouchpadGesturePreset? ReadThreeFingerPreset() =>
        ToPreset(ReadRaw(ThreeFingerSlide));

    public static TouchpadGesturePreset? ReadFourFingerPreset() =>
        ToPreset(ReadRaw(FourFingerSlide));

    public static void WriteThreeFingerPreset(TouchpadGesturePreset preset) =>
        WritePreset(ThreeFingerSlide, preset);

    public static void WriteFourFingerPreset(TouchpadGesturePreset preset) =>
        WritePreset(FourFingerSlide, preset);

    public static int? ReadRaw(string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RegistryKey, writable: false);
        return key?.GetValue(name) is int value ? value : null;
    }

    public static void WriteRaw(string name, int value)
    {
        if (value < 0)
        {
            using var existing = Registry.CurrentUser.OpenSubKey(RegistryKey, writable: true);
            existing?.DeleteValue(name, throwOnMissingValue: false);
            return;
        }

        using var key = Registry.CurrentUser.CreateSubKey(RegistryKey, writable: true)
            ?? throw new InvalidOperationException("Touchpad settings key not found.");
        key.SetValue(name, value, RegistryValueKind.DWord);
    }

    private static void WritePreset(string name, TouchpadGesturePreset preset)
    {
        if (!Enum.IsDefined(preset))
            throw new ArgumentOutOfRangeException(nameof(preset));
        WriteRaw(name, (int)preset);
    }

    private static TouchpadGesturePreset? ToPreset(int? value) =>
        value is >= 0 and <= 3 ? (TouchpadGesturePreset)value.Value : null;
}

public static class TouchpadSettingsStore
{
    public const string RegistryKey = @"Software\Microsoft\Windows\CurrentVersion\PrecisionTouchPad";
    public const int DefaultCursorSpeed = 10;
    public const int MaxCursorSpeed = 20;

    public static int ReadCursorSpeed() =>
        Math.Clamp(ReadDword("CursorSpeed", DefaultCursorSpeed), 0, MaxCursorSpeed);

    public static void WriteCursorSpeed(int value)
    {
        if (value is < 0 or > MaxCursorSpeed)
            throw new ArgumentOutOfRangeException(nameof(value), "CursorSpeed is 0..20.");
        WriteDword("CursorSpeed", value);
    }

    public static bool IsTapToClickEnabled => ReadEnabled("TapsEnabled", true);
    public static void SetTapToClick(bool enabled) => WriteEnabled("TapsEnabled", enabled);

    public static bool IsZoomEnabled => ReadEnabled("ZoomEnabled", true);
    public static void SetZoomEnabled(bool enabled) => WriteEnabled("ZoomEnabled", enabled);

    public static bool IsTwoFingerTapEnabled => ReadEnabled("TwoFingerTapEnabled", true);
    public static bool IsRightClickZoneEnabled => ReadEnabled("RightClickZoneEnabled", false);

    public static void SetSecondaryClickMode(int index)
    {
        switch (index)
        {
            case 0:
                WriteEnabled("TwoFingerTapEnabled", false);
                WriteEnabled("RightClickZoneEnabled", false);
                break;
            case 1:
                WriteEnabled("TwoFingerTapEnabled", true);
                WriteEnabled("RightClickZoneEnabled", false);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(index), "Secondary click mode is 0 or 1.");
        }
    }

    public static bool IsNaturalScrolling => ReadDword("ScrollDirection", 0) == 0;

    public static void SetNaturalScrolling(bool natural) =>
        WriteDword("ScrollDirection", natural ? 0 : 1);

    private static int ReadDword(string name, int fallback)
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RegistryKey, writable: false);
        return key?.GetValue(name) is int value ? value : fallback;
    }

    private static void WriteDword(string name, int value)
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(RegistryKey, writable: true)
            ?? throw new InvalidOperationException("Touchpad settings key not found.");
        key.SetValue(name, value, Microsoft.Win32.RegistryValueKind.DWord);
    }

    private static bool ReadEnabled(string name, bool fallback) =>
        ReadDword(name, fallback ? -1 : 0) != 0;

    private static void WriteEnabled(string name, bool enabled) =>
        WriteDword(name, enabled ? -1 : 0);
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
