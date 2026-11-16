using System.IO;
using System.Security.Principal;
using System.Windows.Threading;
using OpenTrackpad.Core.Haptics;
using OpenTrackpad.Interop.Drag;
using OpenTrackpad.Interop.Driver;
using OpenTrackpad.Interop.Input;

namespace OpenTrackpad.App;

public sealed class AppHost : IDisposable
{
    private readonly Dispatcher _dispatcher;
    private bool _disposed;

    public AppHost()
    {
        _dispatcher = Dispatcher.CurrentDispatcher;
        Pipe = new NamedPipeControlPanelPipe();
        Drag = new ThreeFingerDragService();
        Battery = new BatteryMonitor(Pipe);
        Settings = AppSettingsStore.Load();
        if (!Settings.TrackingSpeedSeeded)
        {
            SetCursorSpeed(TouchpadSettingsStore.DefaultCursorSpeed);
            Settings.TrackingSpeedSeeded = true;
            AppSettingsStore.Save(Settings);
        }

        Battery.LevelChanged += level =>
            _dispatcher.BeginInvoke(() => BatteryLevelChanged?.Invoke(level));
        Battery.AlertRaised += alert =>
            _dispatcher.BeginInvoke(() => BatteryAlert?.Invoke(alert));
        Battery.Error += error =>
        {
            AppLog.Error("Battery: " + error);
            _dispatcher.BeginInvoke(() => BatteryError?.Invoke(error));
        };
        Drag.StatusChanged += status =>
            _dispatcher.BeginInvoke(() => DragStatusChanged?.Invoke(status));
    }

    public NamedPipeControlPanelPipe Pipe { get; }
    public ThreeFingerDragService Drag { get; }
    public BatteryMonitor Battery { get; }
    public AppSettings Settings { get; private set; }

    public event Action<int>? BatteryLevelChanged;
    public event Action<Core.Battery.BatteryAlertKind>? BatteryAlert;
    public event Action<string>? BatteryError;
    public event Action<string>? DragStatusChanged;

    public bool IsAdministrator
    {
        get
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
    }

    public void Start()
    {
        Battery.Start();

        if (Settings.DragEnabled)
        {
            try
            {
                Drag.Start();
            }
            catch (Exception ex)
            {
                DragStatusChanged?.Invoke($"เริ่มลาก 3 นิ้วไม่สำเร็จ: {ex.Message}");
                Settings.DragEnabled = false;
                AppSettingsStore.Save(Settings);
            }
        }
    }

    public void SetDragEnabled(bool enabled)
    {
        if (enabled == Drag.IsEnabled)
        {
            Settings.DragEnabled = enabled;
            AppSettingsStore.Save(Settings);
            return;
        }

        if (enabled)
            Drag.Start();
        else
            Drag.Stop();

        Settings.DragEnabled = enabled;
        AppSettingsStore.Save(Settings);
    }

    public void RefreshBattery() => Battery.Refresh();

    public void ApplyHaptics(HapticSettings haptics)
    {
        DriverSettings current = DriverSettingsStore.Read();
        DriverSettings updated = current with
        {
            FeedbackClick = haptics.ClickStrength,
            FeedbackRelease = haptics.ReleaseStrength,
        };

        WriteDriverSettings(updated);
        Settings.QuietClickBackup = null;
        AppSettingsStore.Save(Settings);
        ControlPanelResponse response = Pipe.ReloadSettings();
        if (!response.Ok)
            throw new IOException($"ไดรเวอร์ reload ตอบสถานะ 0x{response.Status:X2}");
    }

    public void ApplyPointer(DriverSettings settings)
    {
        WriteDriverSettings(settings);
        ControlPanelResponse response = Pipe.ReloadSettings();
        if (!response.Ok)
            throw new IOException($"ไดรเวอร์ reload ตอบสถานะ 0x{response.Status:X2}");
    }

    public bool IsQuietClickActive()
    {
        DriverSettings settings = DriverSettingsStore.Read();
        return settings.FeedbackClick == 0 && settings.FeedbackRelease == 0;
    }

    public void SetQuietClick(bool enabled)
    {
        DriverSettings current = DriverSettingsStore.Read();
        if (enabled)
        {
            if (Settings.QuietClickBackup is null)
            {
                Settings.QuietClickBackup = IsQuietClickActive()
                    ? HapticSettings.Default
                    : new HapticSettings(current.FeedbackClick, current.FeedbackRelease);
                AppSettingsStore.Save(Settings);
            }

            WriteDriverSettings(current with
            {
                FeedbackClick = 0,
                FeedbackRelease = 0,
            });
        }
        else
        {
            HapticSettings restore = Settings.QuietClickBackup ?? HapticSettings.Default;
            WriteDriverSettings(current with
            {
                FeedbackClick = restore.ClickStrength,
                FeedbackRelease = restore.ReleaseStrength,
            });
            Settings.QuietClickBackup = null;
            AppSettingsStore.Save(Settings);
        }

        ControlPanelResponse response = Pipe.ReloadSettings();
        if (!response.Ok)
            throw new IOException($"ไดรเวอร์ reload ตอบสถานะ 0x{response.Status:X2}");
    }

    public void SetCursorSpeed(int value)
    {
        value = Math.Clamp(value, 1, 20);
        try
        {
            TouchpadSettingsApi.Update(p => p.CursorSpeed = (uint)value);
        }
        catch
        {
            MouseSpeed.Set(value);
            try { TouchpadSettingsStore.WriteCursorSpeed(value); } catch { }
        }
    }

    public int GetCursorSpeed()
    {
        try
        {
            var parameters = TouchpadSettingsApi.Get();
            if (parameters is not null && parameters.CursorSpeed is >= 1 and <= 20)
                return (int)parameters.CursorSpeed;
        }
        catch
        {
            // Fall through to registry/system speed.
        }

        return MouseSpeed.Get();
    }

    public bool IsTapToClickEnabled() =>
        ReadTouchpadBool(p => p.TapEnabled, TouchpadSettingsStore.IsTapToClickEnabled);

    public void SetTapToClick(bool enabled) =>
        UpdateTouchpad(p => p.TapEnabled = enabled,
            () => TouchpadSettingsStore.SetTapToClick(enabled));

    public bool IsZoomEnabled() =>
        ReadTouchpadBool(p => p.ZoomEnabled, TouchpadSettingsStore.IsZoomEnabled);

    public void SetZoomEnabled(bool enabled) =>
        UpdateTouchpad(p => p.ZoomEnabled = enabled,
            () => TouchpadSettingsStore.SetZoomEnabled(enabled));

    public bool IsNaturalScrolling() =>
        ReadTouchpadBool(p => !p.ScrollDirectionReversed, TouchpadSettingsStore.IsNaturalScrolling);

    public void SetNaturalScrolling(bool natural) =>
        UpdateTouchpad(p => p.ScrollDirectionReversed = !natural,
            () => TouchpadSettingsStore.SetNaturalScrolling(natural));

    public int GetSecondaryClickMode()
    {
        try
        {
            var parameters = TouchpadSettingsApi.Get();
            if (parameters is not null)
                return parameters.TwoFingerTapEnabled ? 1 : 0;
        }
        catch
        {
            // Fall through.
        }

        return TouchpadSettingsStore.IsTwoFingerTapEnabled ? 1 : 0;
    }

    public void SetSecondaryClickMode(int index)
    {
        if (index is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(index));

        UpdateTouchpad(
            p =>
            {
                p.TwoFingerTapEnabled = index == 1;
                p.RightClickZoneEnabled = false;
            },
            () => TouchpadSettingsStore.SetSecondaryClickMode(index));
    }

    private static bool ReadTouchpadBool(
        Func<TouchpadSettingsApi.TouchpadParameters, bool> selector, bool fallback)
    {
        try
        {
            var parameters = TouchpadSettingsApi.Get();
            if (parameters is not null)
                return selector(parameters);
        }
        catch
        {
            // Fall through.
        }
        return fallback;
    }

    private static void UpdateTouchpad(
        Action<TouchpadSettingsApi.TouchpadParameters> mutate, Action fallback)
    {
        try
        {
            TouchpadSettingsApi.Update(mutate);
        }
        catch
        {
            fallback();
        }
    }

    private static void WriteDriverSettings(DriverSettings settings)
    {
        try
        {
            DriverSettingsStore.Write(settings);
        }
        catch (AdministratorRequiredException)
        {
            ElevatedSettingsApplier.Apply(settings);
        }
    }

    public void ApplyScrollSensitivity(int value)
    {
        if (Settings.OriginalPanSensitivity is null)
            Settings.OriginalPanSensitivity = ScrollSettingsStore.ReadPanSensitivity();

        ScrollSettingsStore.WritePanSensitivity(value);
        AppSettingsStore.Save(Settings);
    }

    public void RestoreScrollSensitivity()
    {
        if (Settings.OriginalPanSensitivity is null)
            return;

        ScrollSettingsStore.WritePanSensitivity(Settings.OriginalPanSensitivity.Value);
        Settings.OriginalPanSensitivity = null;
        AppSettingsStore.Save(Settings);
    }

    public int ReadScrollSensitivity() => ScrollSettingsStore.ReadPanSensitivity();

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Battery.Dispose();
        Drag.Dispose();
    }
}
