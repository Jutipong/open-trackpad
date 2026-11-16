using System.Diagnostics;
using System.IO;
using System.Windows;
using OpenTrackpad.Core.Battery;
using OpenTrackpad.Interop.Input;

namespace OpenTrackpad.App;

public partial class App : Application
{
    private Mutex? _mutex;
    private bool _ownsMutex;
    private EventWaitHandle? _showWindowEvent;
    private AppHost? _host;
    private TrayIconHost? _tray;
    private MainWindow? _window;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (ElevatedSettingsApplier.TryApplyFromArguments(e.Args, out int helperExitCode))
        {
            Shutdown(helperExitCode);
            return;
        }

        if (TryRunDragWatchdog(e.Args))
            return;

        try
        {
            _showWindowEvent = new EventWaitHandle(
                initialState: false,
                EventResetMode.AutoReset,
                @"Local\OpenTrackpad.ShowWindow");
        }
        catch (Exception ex)
        {
            AppLog.Error("Create show-window event failed", ex);
        }

        _mutex = new Mutex(initiallyOwned: true, @"Local\OpenTrackpad.SingleInstance", out bool createdNew);
        _ownsMutex = createdNew;
        if (!createdNew)
        {
            try
            {
                _showWindowEvent?.Set();
            }
            catch (Exception ex)
            {
                AppLog.Error("Signal running instance failed", ex);
            }

            Shutdown();
            return;
        }

        DispatcherUnhandledException += (_, args) =>
        {
            LogError(args.Exception);
            MessageBox.Show(
                $"เกิดข้อผิดพลาด:\n{args.Exception.Message}",
                "OpenTrackpad",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            args.Handled = true;
        };

        try
        {
            _host = new AppHost();
            _window = new MainWindow(_host);
            _tray = new TrayIconHost();

            _tray.OpenRequested += ShowWindow;
            _tray.ExitRequested += ExitApplication;
            _window.HiddenToTray += OnHiddenToTray;

            _host.BatteryLevelChanged += level =>
            {
                _tray.SetBattery(level);
                _window?.SetBattery(level);
            };
            _host.BatteryError += error =>
            {
                _tray.SetBatteryUnavailable();
                _window?.SetBatteryError(error);
            };
            _host.BatteryAlert += ShowBatteryAlert;
            _host.DragStatusChanged += status =>
            {
                _window?.SetDragStatus(status);
            };

            _host.Start();
            _window.SetDragChecked(_host.Drag.IsEnabled);
            _host.RefreshBattery();
            ShowWindow();
            StartShowWindowListener();
        }
        catch (Exception ex)
        {
            LogError(ex);
            MessageBox.Show(
                $"เริ่ม OpenTrackpad ไม่สำเร็จ:\n{ex.Message}",
                "OpenTrackpad",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown();
        }
    }

    private bool TryRunDragWatchdog(string[] args)
    {
        if (args.Length < 2 ||
            !string.Equals(args[0], "--drag-watchdog", StringComparison.Ordinal) ||
            !int.TryParse(args[1], out int processId))
        {
            return false;
        }

        try
        {
            using var process = Process.GetProcessById(processId);
            process.WaitForExit();
        }
        catch
        {
            // The main process is already gone.
        }

        var injector = new SendInputMouseInjector();
        for (int attempt = 0; attempt < 3; attempt++)
        {
            try { injector.LeftUp(); } catch { }
            Thread.Sleep(1);
        }

        Shutdown(0);
        return true;
    }

    private void ShowBatteryAlert(BatteryAlertKind kind)
    {
        if (_host is null || _tray is null) return;

        int level = _host.Battery.LastLevel;
        if (kind == BatteryAlertKind.Critical)
            _tray.ShowAlert("แบตเตอรี่ต่ำมาก", $"Magic Trackpad เหลือ {level}% กรุณาชาร์จ", critical: true);
        else
            _tray.ShowAlert("แบตเตอรี่ต่ำ", $"Magic Trackpad เหลือ {level}%", critical: false);
    }

    private void ShowWindow()
    {
        if (_window is null) return;
        _window.Show();
        if (_window.WindowState == WindowState.Minimized)
            _window.WindowState = WindowState.Normal;
        _window.Activate();
    }

    private void StartShowWindowListener()
    {
        if (_showWindowEvent is null) return;

        var thread = new Thread(() =>
        {
            try
            {
                while (_showWindowEvent.WaitOne())
                    Dispatcher.BeginInvoke(ShowWindow);
            }
            catch (ObjectDisposedException)
            {
                // Application is exiting.
            }
            catch (Exception ex)
            {
                AppLog.Error("Show-window listener stopped", ex);
            }
        })
        {
            IsBackground = true,
            Name = "OpenTrackpad.ShowWindow",
        };
        thread.Start();
    }

    private void OnHiddenToTray()
    {
        if (_host is null || _tray is null) return;
        if (_host.Settings.TrayHintShown) return;

        _host.Settings.TrayHintShown = true;
        AppSettingsStore.Save(_host.Settings);
        _tray.ShowAlert(
            "OpenTrackpad ยังทำงานอยู่",
            "โปรแกรมซ่อนอยู่ที่ tray — ดับเบิลคลิกไอคอนเพื่อเปิดหน้าต่าง หรือคลิกขวา > ออก เพื่อปิดโปรแกรม",
            critical: false);
    }

    private void ExitApplication()
    {
        _host?.Dispose();
        _tray?.Dispose();
        _window?.CloseForExit();
        Shutdown();
    }

    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        _host?.Drag.Stop();
        base.OnSessionEnding(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _host?.Dispose();
        _tray?.Dispose();
        _showWindowEvent?.Dispose();

        if (_ownsMutex)
            _mutex?.ReleaseMutex();
        _mutex?.Dispose();

        base.OnExit(e);
    }

    private static void LogError(Exception ex)
    {
        AppLog.Error("Unhandled exception", ex);
    }
}
