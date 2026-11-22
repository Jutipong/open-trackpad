using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using Microsoft.Win32;
using OpenTrackpad.Core.Input;
using OpenTrackpad.Interop.Input;
using OpenTrackpad.Interop.Ptp;
using OpenTrackpad.Interop.RawInput;

namespace OpenTrackpad.Interop.Drag;

public sealed class ThreeFingerDragService : IDisposable
{
    private const string PrecisionTouchpadKey =
        @"Software\Microsoft\Windows\CurrentVersion\PrecisionTouchPad";
    private const string ThreeFingerSlideValue = "ThreeFingerSlideEnabled";
    private const string FourFingerSlideValue = "FourFingerSlideEnabled";
    private static readonly string ThreeFingerBackupFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "OpenTrackpad",
        "three-finger-slide.backup");
    private static readonly string FourFingerBackupFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "OpenTrackpad",
        "four-finger-slide.backup");

    private readonly ThreeFingerDragFsm _fsm;
    private readonly SwipeGestureFsm _swipeThree;
    private readonly SwipeGestureFsm _swipeFour;
    private readonly RawInputTouchpadSource _source;
    private readonly IMouseInjector _mouse;
    private readonly IKeyboardInjector _keyboard;
    private readonly Timer _watchdog;
    private readonly object _gate = new();

    private bool _running;
    private bool _dragEnabled;
    private SwipeGestureSettings _swipeSettings = SwipeGestureSettings.None;
    private bool _disposed;
    private bool _leftHeld;
    private int? _originalThreeFingerSlide;
    private int? _originalFourFingerSlide;
    private Process? _watchdogProcess;

    public ThreeFingerDragService(
        IMouseInjector? mouse = null,
        ThreeFingerDragOptions? options = null,
        Func<string, bool>? isTargetDevice = null,
        IKeyboardInjector? keyboard = null)
    {
        _mouse = mouse ?? new SendInputMouseInjector();
        _keyboard = keyboard ?? new SendInputKeyboardInjector();
        _fsm = new ThreeFingerDragFsm(options);
        // Magic Trackpad 2 reports about 60 logical units per millimetre.
        // 500 units is roughly 8 mm; 2 units/ms is roughly 33 mm/s.
        _swipeThree = new SwipeGestureFsm(new SwipeGestureOptions
        {
            RequiredContacts = 3,
            HorizontalOnly = true,
            StartThreshold = 500,
            CandidateThreshold = 20,
            MinSpeedPxPerMs = 2.0,
        });
        _swipeFour = new SwipeGestureFsm(new SwipeGestureOptions
        {
            RequiredContacts = 4,
            StartThreshold = 500,
            CandidateThreshold = 20,
            MinSpeedPxPerMs = 1.5,
        });
        _source = new RawInputTouchpadSource(isTargetDevice);
        _source.ReportReceived += OnReport;
        _source.Error += ex => StatusChanged?.Invoke($"Raw Input error: {ex.Message}");
        _watchdog = new Timer(_ => Watchdog(), null, 100, 100);
    }

    public event Action<string>? StatusChanged;
    public event Action<string>? SwipeStatusChanged;

    public bool DragEnabled
    {
        get { lock (_gate) return _dragEnabled; }
    }

    public bool SwipeGesturesEnabled
    {
        get { lock (_gate) return _swipeSettings.AnyEnabled; }
    }

    public double Gain
    {
        get { lock (_gate) return _fsm.Gain; }
    }

    public void SetGain(double gain)
    {
        lock (_gate) _fsm.Gain = gain;
    }

    /// <summary>
    /// Applies the persisted feature flags and starts the raw input source when
    /// at least one gesture family is enabled.
    /// </summary>
    public void Configure(bool dragEnabled, SwipeGestureSettings swipeSettings)
    {
        ArgumentNullException.ThrowIfNull(swipeSettings);
        lock (_gate)
        {
            ThrowIfDisposed();
            _dragEnabled = dragEnabled;
            _swipeSettings = swipeSettings;
            RecoverStaleGestureBackups();
            try
            {
                UpdateRunningState();
            }
            catch
            {
                _dragEnabled = false;
                _swipeSettings = SwipeGestureSettings.None;
                StopRunningCore();
                throw;
            }
        }

        StatusChanged?.Invoke(dragEnabled ? "3-finger drag enabled" : "3-finger drag disabled");
        if (swipeSettings.AnyEnabled)
            SwipeStatusChanged?.Invoke("Swipe gestures enabled");
    }

    public void SetDragEnabled(bool enabled)
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            if (_dragEnabled == enabled) return;

            _dragEnabled = enabled;
            if (!enabled)
            {
                Execute(_fsm.Reset("disabled"));
                if (_leftHeld)
                    SafeLeftUp();
            }

            try
            {
                UpdateRunningState();
                if (_running)
                {
                    if (enabled)
                    {
                        if (_watchdogProcess is null)
                            StartWatchdog();
                    }
                    else
                    {
                        StopWatchdog();
                    }
                }
            }
            catch
            {
                _dragEnabled = !enabled;
                if (!_dragEnabled && !_swipeSettings.AnyEnabled)
                    StopRunningCore();
                throw;
            }
        }

        StatusChanged?.Invoke(enabled ? "3-finger drag enabled" : "3-finger drag disabled");
    }

    public void SetSwipeGestureSettings(SwipeGestureSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        bool stateChanged;
        lock (_gate)
        {
            ThrowIfDisposed();
            if (_swipeSettings == settings) return;

            SwipeGestureSettings previous = _swipeSettings;
            _swipeSettings = settings;
            stateChanged = previous.AnyEnabled != settings.AnyEnabled;
            if (!settings.SwitchDesktop)
                _swipeThree.Reset();
            if (!settings.AnyFourFingerEnabled)
                _swipeFour.Reset();

            try
            {
                UpdateRunningState();
            }
            catch
            {
                _swipeSettings = previous;
                if (!_dragEnabled && !_swipeSettings.AnyEnabled)
                    StopRunningCore();
                throw;
            }
        }

        if (stateChanged)
        {
            SwipeStatusChanged?.Invoke(
                settings.AnyEnabled ? "Swipe gestures enabled" : "Swipe gestures disabled");
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _dragEnabled = false;
            _swipeSettings = SwipeGestureSettings.None;
            StopRunningCore();
        }

        StatusChanged?.Invoke("3-finger drag disabled");
    }

    private void UpdateRunningState()
    {
        bool anyEnabled = _dragEnabled || _swipeSettings.AnyEnabled;
        if (!anyEnabled)
        {
            StopRunningCore();
            return;
        }

        if (!_running)
            StartRunningCore();
        else
            UpdateSlideSuppression();
    }

    private void StartRunningCore()
    {
        UpdateSlideSuppression();
        try
        {
            _source.Start();
            if (_dragEnabled)
                StartWatchdog();
            _running = true;
        }
        catch
        {
            StopWatchdog();
            RestoreAllSlides();
            throw;
        }
    }

    private void StopRunningCore()
    {
        Execute(_fsm.Reset("stopped"));
        if (_leftHeld)
            SafeLeftUp();
        _swipeThree.Reset();
        _swipeFour.Reset();
        _source.Stop();
        StopWatchdog();
        _running = false;
        RestoreAllSlides();
    }

    private void OnReport(byte[] data)
    {
        lock (_gate)
        {
            if (!_running) return;

            try
            {
                PtpInputReport report = Mt2PtpReportParser.Parse(data);
                TouchFrame frame = report.Touch;

                bool threeEnabled = _swipeSettings.SwitchDesktop;
                bool fourEnabled = _swipeSettings.AnyFourFingerEnabled;
                if (threeEnabled || fourEnabled)
                {
                    SwipeGesture? three = null;
                    if (threeEnabled)
                    {
                        if (!_fsm.IsDragging)
                            three = _swipeThree.Process(frame);
                        else
                            _swipeThree.LockUntilLift();
                    }

                    SwipeGesture? four = fourEnabled ? _swipeFour.Process(frame) : null;
                    SwipeGesture? swipe = three ?? four;
                    if (swipe is { } gesture && _swipeSettings.IsEnabled(gesture))
                    {
                        if (_fsm.IsDragging)
                            Execute(_fsm.Reset("swipe gesture"));
                        if (_leftHeld)
                            SafeLeftUp();

                        SendSwipeShortcut(gesture);
                        return;
                    }

                    // A fast horizontal 3-finger swipe is still possible. Keep
                    // drag from moving a window while the swipe is being decided.
                    if (_dragEnabled && threeEnabled && !_fsm.IsDragging && _swipeThree.IsCandidate)
                    {
                        _fsm.Reset("swipe candidate");
                        return;
                    }
                }

                if (_dragEnabled)
                    Execute(_fsm.Process(frame));
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke($"PTP report rejected: {ex.Message}");
            }
        }
    }

    private void SendSwipeShortcut(SwipeGesture gesture)
    {
        if (!_swipeSettings.IsEnabled(gesture)) return;

        IReadOnlyList<ushort>? keys = SwipeGestureShortcuts.For(gesture);
        if (keys is null) return;

        try
        {
            _keyboard.SendShortcut(keys);
            SwipeStatusChanged?.Invoke(SwipeGestureShortcuts.Describe(gesture));
        }
        catch (Exception ex)
        {
            SwipeStatusChanged?.Invoke($"Shortcut failed: {ex.Message}");
        }
    }

    private void Watchdog()
    {
        lock (_gate)
        {
            if (!_running) return;
            Execute(_fsm.Tick(Environment.TickCount64));
        }
    }

    private void Execute(IReadOnlyList<DragAction> actions)
    {
        foreach (var action in actions)
        {
            switch (action.Kind)
            {
                case DragActionKind.Start:
                    // A stuck synthetic down from an earlier attempt must be
                    // released before starting a new drag.
                    if (_leftHeld)
                        SafeLeftUp();

                    if (_mouse.IsLeftButtonDown())
                    {
                        // The user is physically holding the button. Abort the
                        // gesture without injecting LeftUp: the down is not ours.
                        ResetWithoutRelease("left button already down");
                        break;
                    }

                    try
                    {
                        _mouse.LeftDown();
                        _leftHeld = true;
                        StatusChanged?.Invoke("DRAG START");
                    }
                    catch (Exception ex)
                    {
                        ResetWithoutRelease("left down failed");
                        StatusChanged?.Invoke($"LeftDown failed: {ex.Message}");
                    }
                    break;

                case DragActionKind.Move:
                    try
                    {
                        _mouse.Move(action.DeltaX, action.DeltaY);
                    }
                    catch (Exception ex)
                    {
                        Execute(_fsm.Reset("move failed"));
                        StatusChanged?.Invoke($"Move failed: {ex.Message}");
                    }
                    break;

                case DragActionKind.End:
                    if (_leftHeld)
                        SafeLeftUp();
                    StatusChanged?.Invoke($"DRAG END {action.Reason}");
                    break;
            }
        }
    }

    private void ResetWithoutRelease(string reason)
    {
        // The button is not ours, so the End action must not inject LeftUp.
        _fsm.Reset(reason);
        _leftHeld = false;
    }

    private bool SafeLeftUp()
    {
        for (int attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                _mouse.LeftUp();
                _leftHeld = false;
                return true;
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke($"LeftUp attempt {attempt + 1} failed: {ex.Message}");
                Thread.Sleep(1);
            }
        }

        return false;
    }

    private void UpdateSlideSuppression()
    {
        bool needThree = _dragEnabled || _swipeSettings.SwitchDesktop;
        bool needFour = _swipeSettings.AnyFourFingerEnabled;

        SetSlideSuppressed(
            ThreeFingerSlideValue, ThreeFingerBackupFile, needThree, ref _originalThreeFingerSlide);
        SetSlideSuppressed(
            FourFingerSlideValue, FourFingerBackupFile, needFour, ref _originalFourFingerSlide);
    }

    private void SetSlideSuppressed(
        string valueName,
        string backupFile,
        bool suppress,
        ref int? original)
    {
        if (suppress)
        {
            if (original is not null) return;

            try
            {
                using RegistryKey? key =
                    Registry.CurrentUser.OpenSubKey(PrecisionTouchpadKey, writable: true);
                if (key is null) return;

                object? value = key.GetValue(valueName);
                if (value is not int current || current == 0) return;

                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(backupFile)!);
                    File.WriteAllText(
                        backupFile,
                        current.ToString(CultureInfo.InvariantCulture));
                }
                catch (Exception ex)
                {
                    StatusChanged?.Invoke(
                        $"Could not back up Windows gesture {valueName}: {ex.Message}");
                    return;
                }

                key.SetValue(valueName, 0, RegistryValueKind.DWord);
                original = current;
                StatusChanged?.Invoke(
                    $"Windows gesture {valueName} disabled (was 0x{current:X8}); restored on exit.");
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke(
                    $"Could not disable Windows gesture {valueName}: {ex.Message}");
            }

            return;
        }

        if (original is null) return;

        try
        {
            using RegistryKey? key =
                Registry.CurrentUser.OpenSubKey(PrecisionTouchpadKey, writable: true);
            key?.SetValue(valueName, original.Value, RegistryValueKind.DWord);
            try { File.Delete(backupFile); } catch { }
            StatusChanged?.Invoke(
                $"Windows gesture {valueName} restored to 0x{original.Value:X8}.");
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke(
                $"Could not restore Windows gesture {valueName}: {ex.Message}");
        }
        finally
        {
            original = null;
        }
    }

    private void RestoreAllSlides()
    {
        SetSlideSuppressed(
            ThreeFingerSlideValue, ThreeFingerBackupFile, false, ref _originalThreeFingerSlide);
        SetSlideSuppressed(
            FourFingerSlideValue, FourFingerBackupFile, false, ref _originalFourFingerSlide);
    }

    private void RecoverStaleGestureBackups()
    {
        RecoverStaleBackup(ThreeFingerSlideValue, ThreeFingerBackupFile);
        RecoverStaleBackup(FourFingerSlideValue, FourFingerBackupFile);
    }

    private void RecoverStaleBackup(string valueName, string backupFile)
    {
        try
        {
            if (!File.Exists(backupFile)) return;

            string text = File.ReadAllText(backupFile).Trim();
            if (!int.TryParse(
                    text,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out int original))
            {
                File.Delete(backupFile);
                return;
            }

            using RegistryKey? key =
                Registry.CurrentUser.OpenSubKey(PrecisionTouchpadKey, writable: true);
            if (key is not null &&
                key.GetValue(valueName) is int current &&
                current == 0)
            {
                key.SetValue(valueName, original, RegistryValueKind.DWord);
                StatusChanged?.Invoke(
                    $"Recovered Windows gesture {valueName} from a previous crash (0x{original:X8}).");
            }

            File.Delete(backupFile);
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke(
                $"Could not recover Windows gesture {valueName}: {ex.Message}");
        }
    }

    private void StartWatchdog()
    {
        try
        {
            string? exe = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(exe))
                return;

            bool isDotnet = Path.GetFileName(exe).Equals("dotnet.exe", StringComparison.OrdinalIgnoreCase);
            string arguments;
            if (isDotnet)
            {
                string? dll = Assembly.GetEntryAssembly()?.Location;
                if (string.IsNullOrWhiteSpace(dll))
                    return;
                arguments = $"\"{dll}\" --drag-watchdog {Environment.ProcessId}";
            }
            else
            {
                arguments = $"--drag-watchdog {Environment.ProcessId}";
            }

            _watchdogProcess = Process.Start(new ProcessStartInfo
            {
                FileName = exe,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
            });
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke($"Drag watchdog could not start: {ex.Message}");
            _watchdogProcess = null;
        }
    }

    private void StopWatchdog()
    {
        Process? process = _watchdogProcess;
        _watchdogProcess = null;
        if (process is null) return;

        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch
        {
            // The watchdog exits by itself when the main process exits.
        }
        finally
        {
            process.Dispose();
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(ThreeFingerDragService));
    }

    public void Dispose()
    {
        if (_disposed) return;

        lock (_gate)
        {
            if (_disposed) return;

            _dragEnabled = false;
            _swipeSettings = SwipeGestureSettings.None;
            StopRunningCore();
            StopWatchdog();
            _disposed = true;
        }

        _watchdog.Dispose();
        _source.Dispose();
    }
}
