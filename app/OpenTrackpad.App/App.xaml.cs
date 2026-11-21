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
        Loc.Initialize(AppSettingsStore.Load().Language);

        if (ElevatedSettingsApplier.TryApplyFromArguments(e.Args, out int helperExitCode))
        {
            Shutdown(helperExitCode);
            return;
        }

        if (TryRunDragWatchdog(e.Args))
            return;

        if (TryExportDiagnosticsFromCli(e.Args, out int exportExitCode))
        {
            Shutdown(exportExitCode);
            return;
        }

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
                Loc.Format("Dialog.UnhandledError", args.Exception.Message),
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
            _tray.ApplyLanguage();
            Loc.LanguageChanged += OnLanguageChanged;

            _tray.OpenRequested += ShowWindow;
            _tray.ReleaseLeftButtonRequested += ReleaseLeftButton;
            _tray.ExitRequested += ExitApplication;
            _window.HiddenToTray += OnHiddenToTray;

            _host.BatteryLevelChanged += reading =>
            {
                _tray.SetBattery(reading.Level);
                _window?.SetBattery(reading);
                _window?.RefreshHealth();
            };
            _host.BatteryError += error =>
            {
                _tray.SetBatteryUnavailable();
                _window?.SetBatteryError(error);
                _window?.RefreshHealth();
            };
            _host.BatteryAlert += ShowBatteryAlert;
            _host.DragStatusChanged += status =>
            {
                _window?.SetDragStatus(status);
            };
            _host.DeviceChanged += change =>
            {
                if (!change.IsConnected)
                {
                    _tray?.SetBatteryUnavailable();
                    _window?.SetDeviceDisconnected();
                }

                _window?.RefreshHealth();
            };

            _host.Start();
            _window.SetDragChecked(_host.Drag.IsEnabled);
            _window.RefreshHealth();
            ShowWindow();
            StartShowWindowListener();
        }
        catch (Exception ex)
        {
            LogError(ex);
            MessageBox.Show(
                Loc.Format("Dialog.StartupFailed", ex.Message),
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

    private static bool TryExportDiagnosticsFromCli(string[] args, out int exitCode)
    {
        exitCode = 0;
        if (args.Length < 1 ||
            !string.Equals(args[0], "--export-diagnostics", StringComparison.Ordinal))
        {
            return false;
        }

        try
        {
            using var host = new AppHost();
            string path = DiagnosticsExporter.Export(host);
            string pointer = Path.Combine(AppPaths.Root, "last-diagnostics.txt");
            File.WriteAllText(pointer, path);
            AppLog.Info("Diagnostics exported via CLI: " + path);
        }
        catch (Exception ex)
        {
            AppLog.Error("CLI diagnostics export failed", ex);
            exitCode = 1;
        }

        return true;
    }

    private void ReleaseLeftButton()
    {
        if (_host is not null && _host.Drag.IsEnabled)
        {
            try
            {
                _host.SetDragEnabled(false);
                _window?.SetDragChecked(false);
            }
            catch (Exception ex)
            {
                AppLog.Error("Release left button: stop drag failed", ex);
            }
        }

        try
        {
            var injector = new SendInputMouseInjector();
            for (int attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    injector.LeftUp();
                }
                catch (Exception ex)
                {
                    AppLog.Error($"Release left button attempt {attempt + 1} failed", ex);
                }
                Thread.Sleep(20);
            }

            AppLog.Info("Tray: left button released");
            _tray?.ShowAlert(
                Loc.T("Tray.ReleaseTitle"),
                Loc.T("Tray.ReleaseBody"),
                critical: false);
        }
        catch (Exception ex)
        {
            AppLog.Error("Tray release left button failed", ex);
        }
    }

    private void ShowBatteryAlert(BatteryAlertKind kind)
    {
        if (_host is null || _tray is null) return;

        int level = _host.Battery.LastLevel;
        if (kind == BatteryAlertKind.Critical)
        {
            _tray.ShowAlert(
                Loc.T("Tray.CriticalTitle"),
                Loc.Format("Tray.CriticalBody", level),
                critical: true);
        }
        else
        {
            _tray.ShowAlert(
                Loc.T("Tray.LowTitle"),
                Loc.Format("Tray.LowBody", level),
                critical: false);
        }
    }

    private void OnLanguageChanged()
    {
        _tray?.ApplyLanguage();
        _window?.RefreshLocalizedText();
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
            Loc.T("Tray.HintTitle"),
            Loc.T("Tray.HintBody"),
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
        AppLog.Info("OpenTrackpad exiting");
        Loc.LanguageChanged -= OnLanguageChanged;
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
