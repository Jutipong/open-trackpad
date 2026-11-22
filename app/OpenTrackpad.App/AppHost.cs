using System.IO;
using System.Security.Principal;
using System.Windows.Threading;
using OpenTrackpad.Core.Haptics;
using OpenTrackpad.Core.Health;
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
    private string? _lastBatteryError;
    private bool _disposed;

    public AppHost()
    {
        _dispatcher = Dispatcher.CurrentDispatcher;
        Pipe = new NamedPipeControlPanelPipe();
        _control = new TrackpadControlRouter(Pipe);
        Settings = AppSettingsStore.Load();
        if (Settings.SwipeGestures is null)
        {
            Settings.SwipeGestures = Settings.SwipeGesturesEnabled
                ? new SwipeGestureSettings()
                : SwipeGestureSettings.None;
            Settings.SwipeGesturesEnabled = Settings.SwipeGestures.AnyEnabled;
            AppSettingsStore.Save(Settings);
        }
        else if (Settings.SwipeGesturesEnabled != Settings.SwipeGestures.AnyEnabled)
        {
            Settings.SwipeGesturesEnabled = Settings.SwipeGestures.AnyEnabled;
            AppSettingsStore.Save(Settings);
        }

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
            _lastBatteryError = null;
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
            if (!string.Equals(error, _lastBatteryError, StringComparison.Ordinal))
            {
                _lastBatteryError = error;
                AppLog.Error("Battery: " + error);
            }

            _dispatcher.BeginInvoke(() => BatteryError?.Invoke(error));
        };
        Drag.StatusChanged += status =>
        {
            AppLog.Info("Drag: " + status);
            _dispatcher.BeginInvoke(() => DragStatusChanged?.Invoke(status));
        };
        Drag.SwipeStatusChanged += status =>
        {
            AppLog.Info("Swipe: " + status);
            _dispatcher.BeginInvoke(() => SwipeStatusChanged?.Invoke(status));
        };
    }

    public NamedPipeControlPanelPipe Pipe { get; }
    public ThreeFingerDragService Drag { get; }
    public BatteryMonitor Battery { get; }
    public TrackpadDeviceMonitor DeviceMonitor { get; }
    public AppSettings Settings { get; private set; }
    public SwipeGestureSettings SwipeSettings =>
        Settings.SwipeGestures ?? SwipeGestureSettings.None;

    public event Action<BatteryReading>? BatteryLevelChanged;
    public event Action<Core.Battery.BatteryAlertKind>? BatteryAlert;
    public event Action<string>? BatteryError;
    public event Action<string>? DragStatusChanged;
    public event Action<string>? SwipeStatusChanged;
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
            $"swipe={SwipeSettings.AnyEnabled}, lang={Loc.Language}");

        try
        {
            Drag.Configure(Settings.DragEnabled, SwipeSettings);
        }
        catch (Exception ex)
        {
            DragStatusChanged?.Invoke(Loc.Format("Drag.StartFailed", ex.Message));
            Settings.DragEnabled = false;
            Settings.SwipeGestures = SwipeGestureSettings.None;
            Settings.SwipeGesturesEnabled = false;
            AppSettingsStore.Save(Settings);
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
        if (enabled == Drag.DragEnabled)
        {
            Settings.DragEnabled = enabled;
            AppSettingsStore.Save(Settings);
            return;
        }

        Drag.SetDragEnabled(enabled);
        Settings.DragEnabled = enabled;
        AppSettingsStore.Save(Settings);
    }

    public void SetSwipeGestureSettings(SwipeGestureSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (settings == SwipeSettings)
        {
            Settings.SwipeGestures = settings;
            Settings.SwipeGesturesEnabled = settings.AnyEnabled;
            AppSettingsStore.Save(Settings);
            return;
        }

        Drag.SetSwipeGestureSettings(settings);
        Settings.SwipeGestures = settings;
        Settings.SwipeGesturesEnabled = settings.AnyEnabled;
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
        HapticMode currentMode = HapticModes.Detect(
            current.FeedbackClick, current.FeedbackRelease, current.ButtonDisabled);
        bool specialCurrent = currentMode is HapticMode.Maximum or HapticMode.Disabled;
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

    public bool IsTapAndDragEnabled() =>
        ReadTouchpadBool(p => p.TapAndDragEnabled, TouchpadSettingsStore.IsTapAndDragEnabled);

    public void SetTapAndDrag(bool enabled) =>
        UpdateTouchpad(p => p.TapAndDragEnabled = enabled,
            () => TouchpadSettingsStore.SetTapAndDrag(enabled));

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

    public bool IsLeaveOnWithMouse() =>
        ReadTouchpadBool(p => p.AllowActiveWhenMousePresent, TouchpadSettingsStore.IsLeaveOnWithMouse);

    public void SetLeaveOnWithMouse(bool enabled) =>
        UpdateTouchpad(p => p.AllowActiveWhenMousePresent = enabled,
            () => TouchpadSettingsStore.SetLeaveOnWithMouse(enabled));

    public int GetTouchpadSensitivity()
    {
        try
        {
            var parameters = TouchpadSettingsApi.Get();
            if (parameters is not null && parameters.SensitivityLevel <= TouchpadSettingsStore.MaxSensitivity)
                return (int)parameters.SensitivityLevel;
        }
        catch
        {
            // Fall through to registry.
        }

        return TouchpadSettingsStore.ReadSensitivity();
    }

    public void SetTouchpadSensitivity(int level)
    {
        if (level is < 0 or > TouchpadSettingsStore.MaxSensitivity)
            throw new ArgumentOutOfRangeException(nameof(level), "Sensitivity is 0..4.");

        UpdateTouchpad(p => p.SensitivityLevel = (uint)level,
            () => TouchpadSettingsStore.WriteSensitivity(level));
    }

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
            p => p.TwoFingerTapEnabled = index == 1,
            () => TouchpadSettingsStore.SetSecondaryClickMode(index));
    }

    public bool IsRightClickZoneEnabled() =>
        ReadTouchpadBool(p => p.RightClickZoneEnabled, TouchpadSettingsStore.IsRightClickZoneEnabled);

    public void SetRightClickZone(bool enabled) =>
        UpdateTouchpad(p => p.RightClickZoneEnabled = enabled,
            () => TouchpadSettingsStore.SetRightClickZone(enabled));

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

    public HealthInputs GetHealthInputs()
    {
        IReadOnlyList<TrackpadDevice> devices = TrackpadDeviceLocator.FindAll();
        bool usbPresent = devices.Any(d => d.Transport == TrackpadTransport.Usb);
        bool bluetoothPresent = devices.Any(d => d.Transport == TrackpadTransport.Bluetooth);
        bool isAdmin = IsAdministrator;

        return new HealthInputs(
            DriverHealth.IsDriverInstalled(),
            usbPresent,
            bluetoothPresent,
            isAdmin || DriverHealth.IsAccessAllowed(DriverSettingsStore.UsbRegistryKey),
            isAdmin || DriverHealth.IsAccessAllowed(DriverSettingsStore.BluetoothRegistryKey));
    }

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
