using System.IO;
using System.Security.Principal;
using System.Windows.Threading;
using OpenTrackpad.Core.Haptics;
using OpenTrackpad.Core.Input;
using OpenTrackpad.Interop.Drag;
using OpenTrackpad.Interop.Driver;
using OpenTrackpad.Interop.Input;
using OpenTrackpad.Interop.RawInput;

namespace OpenTrackpad.App;

public sealed class AppHost : IDisposable
{
    private readonly Dispatcher _dispatcher;
    private readonly TrackpadControlRouter _control;
    private int _lastLoggedBatteryLevel = -1;
    private bool _disposed;

    public AppHost()
    {
        _dispatcher = Dispatcher.CurrentDispatcher;
        Pipe = new NamedPipeControlPanelPipe();
        _control = new TrackpadControlRouter(Pipe);
        Settings = AppSettingsStore.Load();
        Drag = new ThreeFingerDragService(options: new ThreeFingerDragOptions
        {
            Gain = GetDragGain(),
        });
        Battery = new BatteryMonitor(new TrackpadBatteryReader(Pipe));
        DeviceMonitor = new TrackpadDeviceMonitor();
        DeviceMonitor.Changed += OnDeviceChanged;
        DeviceMonitor.Error += error => AppLog.Error("Device monitor failed", error);
        if (!Settings.TrackingSpeedSeeded)
        {
            SetCursorSpeed(TouchpadSettingsStore.DefaultCursorSpeed);
            Settings.TrackingSpeedSeeded = true;
            AppSettingsStore.Save(Settings);
        }

        Battery.LevelChanged += level =>
        {
            if (level.Level != _lastLoggedBatteryLevel)
            {
                _lastLoggedBatteryLevel = level.Level;
                AppLog.Info($"Battery: {level.Level}% ({level.Transport})");
            }
            _dispatcher.BeginInvoke(() => BatteryLevelChanged?.Invoke(level));
        };
        Battery.AlertRaised += alert =>
        {
            AppLog.Info("Battery alert: " + alert);
            _dispatcher.BeginInvoke(() => BatteryAlert?.Invoke(alert));
        };
        Battery.Error += error =>
        {
            AppLog.Error("Battery: " + error);
            _dispatcher.BeginInvoke(() => BatteryError?.Invoke(error));
        };
        Drag.StatusChanged += status =>
        {
            AppLog.Info("Drag: " + status);
            _dispatcher.BeginInvoke(() => DragStatusChanged?.Invoke(status));
        };
    }

    public NamedPipeControlPanelPipe Pipe { get; }
    public ThreeFingerDragService Drag { get; }
    public BatteryMonitor Battery { get; }
    public TrackpadDeviceMonitor DeviceMonitor { get; }
    public AppSettings Settings { get; private set; }

    public event Action<BatteryReading>? BatteryLevelChanged;
    public event Action<Core.Battery.BatteryAlertKind>? BatteryAlert;
    public event Action<string>? BatteryError;
    public event Action<string>? DragStatusChanged;
    public event Action<TrackpadDeviceChange>? DeviceChanged;

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
        DeviceMonitor.Start();

        AppLog.Info(
            $"OpenTrackpad v{AppVersion.Display} started: os={Environment.OSVersion.Version}, " +
            $"admin={IsAdministrator}, transport={DeviceMonitor.Current.Transport}, " +
            $"devices={DeviceMonitor.Current.Devices.Count}, drag={Settings.DragEnabled}, " +
            $"lang={Loc.Language}");

        if (Settings.DragEnabled)
        {
            try
            {
                Drag.Start();
            }
            catch (Exception ex)
            {
                DragStatusChanged?.Invoke(Loc.Format("Drag.StartFailed", ex.Message));
                Settings.DragEnabled = false;
                AppSettingsStore.Save(Settings);
            }
        }
    }

    public double GetDragGain() =>
        Math.Clamp(Settings.DragGain ?? 0.5, 0.5, 1.5);

    public void SetDragGain(double gain)
    {
        gain = Math.Clamp(gain, 0.5, 1.5);
        Drag.SetGain(gain);
        Settings.DragGain = gain;
        AppSettingsStore.Save(Settings);
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
            ButtonDisabled = false,
        };

        bool appliedElevated = WriteDriverSettings(updated);
        Settings.QuietClickBackup = null;
        AppSettingsStore.Save(Settings);
        if (!appliedElevated)
            ReloadDriverSettings();
    }

    public void ApplyPointer(DriverSettings settings)
    {
        bool appliedElevated = WriteDriverSettings(settings);
        if (!appliedElevated)
            ReloadDriverSettings();
    }

    public HapticMode GetHapticMode()
    {
        DriverSettings settings = DriverSettingsStore.Read();
        return HapticModes.Detect(
            settings.FeedbackClick, settings.FeedbackRelease, settings.ButtonDisabled);
    }

    public void SetHapticMode(HapticMode mode)
    {
        DriverSettings current = DriverSettingsStore.Read();
        bool specialCurrent = HapticModes.Detect(
            current.FeedbackClick, current.FeedbackRelease, current.ButtonDisabled) != HapticMode.Preset;
        HapticSettings preset = Settings.QuietClickBackup
            ?? (specialCurrent
                ? HapticSettings.Default
                : new HapticSettings(current.FeedbackClick, current.FeedbackRelease));

        if (mode == HapticMode.Preset)
        {
            Settings.QuietClickBackup = null;
        }
        else if (Settings.QuietClickBackup is null)
        {
            Settings.QuietClickBackup = specialCurrent
                ? HapticSettings.Default
                : new HapticSettings(current.FeedbackClick, current.FeedbackRelease);
        }

        AppSettingsStore.Save(Settings);

        HapticModeValues values = HapticModes.Resolve(mode, preset);
        bool appliedElevated = WriteDriverSettings(current with
        {
            FeedbackClick = values.Strengths.ClickStrength,
            FeedbackRelease = values.Strengths.ReleaseStrength,
            ButtonDisabled = values.ButtonDisabled,
        });
        if (!appliedElevated)
            ReloadDriverSettings();
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

    public ControlPanelResponse PreviewHaptic(uint strength) =>
        _control.Actuate(strength);

    public TrackpadTransport GetTransport() => _control.DetectTransport();

    public void ReloadDriverSettings()
    {
        ControlPanelResponse response = _control.ReloadSettings();
        if (!response.Ok)
            throw new IOException(Loc.Format("Driver.ReloadFailed", response.Status));
    }

    /// <returns>True when the elevated helper applied and reloaded the settings.</returns>
    private static bool WriteDriverSettings(DriverSettings settings)
    {
        try
        {
            DriverSettingsStore.Write(settings);
            return false;
        }
        catch (AdministratorRequiredException)
        {
            ElevatedSettingsApplier.Apply(settings);
            return true;
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

    public TouchpadGesturePreset? GetThreeFingerPreset() =>
        GestureSettingsStore.ReadThreeFingerPreset();

    public TouchpadGesturePreset? GetFourFingerPreset() =>
        GestureSettingsStore.ReadFourFingerPreset();

    public bool HasGestureBackup =>
        Settings.OriginalThreeFingerSlide is not null ||
        Settings.OriginalFourFingerSlide is not null;

    public void ApplyThreeFingerPreset(TouchpadGesturePreset preset)
    {
        if (Drag.IsEnabled)
            throw new InvalidOperationException(Loc.T("Gesture.DragManaged"));

        if (Settings.OriginalThreeFingerSlide is null)
        {
            Settings.OriginalThreeFingerSlide =
                GestureSettingsStore.ReadRaw(GestureSettingsStore.ThreeFingerSlide) ?? -1;
            AppSettingsStore.Save(Settings);
        }

        GestureSettingsStore.WriteThreeFingerPreset(preset);
    }

    public void ApplyFourFingerPreset(TouchpadGesturePreset preset)
    {
        if (Settings.OriginalFourFingerSlide is null)
        {
            Settings.OriginalFourFingerSlide =
                GestureSettingsStore.ReadRaw(GestureSettingsStore.FourFingerSlide) ?? -1;
            AppSettingsStore.Save(Settings);
        }

        GestureSettingsStore.WriteFourFingerPreset(preset);
    }

    /// <returns>True when the three-finger value was skipped because drag is enabled.</returns>
    public bool RestoreGesturePresets()
    {
        bool skippedThreeFinger = false;

        if (Settings.OriginalThreeFingerSlide is { } threeFinger)
        {
            if (Drag.IsEnabled)
            {
                skippedThreeFinger = true;
            }
            else
            {
                GestureSettingsStore.WriteRaw(GestureSettingsStore.ThreeFingerSlide, threeFinger);
                Settings.OriginalThreeFingerSlide = null;
            }
        }

        if (Settings.OriginalFourFingerSlide is { } fourFinger)
        {
            GestureSettingsStore.WriteRaw(GestureSettingsStore.FourFingerSlide, fourFinger);
            Settings.OriginalFourFingerSlide = null;
        }

        AppSettingsStore.Save(Settings);
        return skippedThreeFinger;
    }

    private void OnDeviceChanged(TrackpadDeviceChange change)
    {
        if (_disposed) return;

        AppLog.Info(
            $"Device changed: transport={change.Transport}, count={change.Devices.Count}");

        if (change.IsConnected)
            ThreadPool.QueueUserWorkItem(_ => Battery.Refresh());

        _dispatcher.BeginInvoke(() => DeviceChanged?.Invoke(change));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        DeviceMonitor.Dispose();
        Battery.Dispose();
        Drag.Dispose();
    }
}
