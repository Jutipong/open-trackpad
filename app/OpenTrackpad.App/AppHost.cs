using System.IO;
using System.Security.Principal;
using System.Windows.Threading;
using OpenTrackpad.Core.Haptics;
using OpenTrackpad.Interop.Drag;
using OpenTrackpad.Interop.Driver;

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
