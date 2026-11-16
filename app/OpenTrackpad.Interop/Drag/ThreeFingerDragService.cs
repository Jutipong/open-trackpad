using System.IO;
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
    private static readonly string GestureBackupFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "OpenTrackpad",
        "three-finger-slide.backup");

    private readonly ThreeFingerDragFsm _fsm;
    private readonly RawInputTouchpadSource _source;
    private readonly IMouseInjector _mouse;
    private readonly Timer _watchdog;
    private readonly object _gate = new();

    private bool _enabled;
    private bool _disposed;
    private int? _originalThreeFingerSlide;

    public ThreeFingerDragService(
        IMouseInjector? mouse = null,
        ThreeFingerDragOptions? options = null,
        string rawInputFilter = "mi_01&col01")
    {
        _mouse = mouse ?? new SendInputMouseInjector();
        _fsm = new ThreeFingerDragFsm(options);
        _source = new RawInputTouchpadSource(rawInputFilter);
        _source.ReportReceived += OnReport;
        _source.Error += ex => StatusChanged?.Invoke($"Raw Input error: {ex.Message}");
        _watchdog = new Timer(_ => Watchdog(), null, 100, 100);
    }

    public event Action<string>? StatusChanged;

    public bool IsEnabled
    {
        get { lock (_gate) return _enabled; }
    }

    public void Start()
    {
        lock (_gate)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(ThreeFingerDragService));
            if (_enabled) return;

            RecoverStaleGestureBackup();
            _originalThreeFingerSlide = DisableWindowsThreeFingerSlide();
            try
            {
                _source.Start();
                _enabled = true;
            }
            catch
            {
                RestoreWindowsThreeFingerSlide();
                throw;
            }
        }

        StatusChanged?.Invoke("3-finger drag enabled");
    }

    public void Stop()
    {
        lock (_gate)
        {
            if (!_enabled) return;

            Execute(_fsm.Reset("disabled"));
            _source.Stop();
            _enabled = false;
            RestoreWindowsThreeFingerSlide();
        }

        StatusChanged?.Invoke("3-finger drag disabled");
    }

    private void OnReport(byte[] data)
    {
        lock (_gate)
        {
            if (!_enabled) return;

            try
            {
                PtpInputReport report = Mt2PtpReportParser.Parse(data);
                Execute(_fsm.Process(report.Touch));
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke($"PTP report rejected: {ex.Message}");
            }
        }
    }

    private void Watchdog()
    {
        lock (_gate)
        {
            if (!_enabled) return;
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
                    if (_mouse.IsLeftButtonDown())
                    {
                        Execute(_fsm.Reset("left button already down"));
                        break;
                    }

                    try
                    {
                        _mouse.LeftDown();
                        StatusChanged?.Invoke("DRAG START");
                    }
                    catch (Exception ex)
                    {
                        Execute(_fsm.Reset("left down failed"));
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
                    SafeLeftUp();
                    StatusChanged?.Invoke($"DRAG END {action.Reason}");
                    break;
            }
        }
    }

    private void SafeLeftUp()
    {
        for (int attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                _mouse.LeftUp();
                return;
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke($"LeftUp attempt {attempt + 1} failed: {ex.Message}");
                Thread.Sleep(1);
            }
        }
    }

    private int? DisableWindowsThreeFingerSlide()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PrecisionTouchpadKey, writable: true);
            if (key is null) return null;

            object? value = key.GetValue(ThreeFingerSlideValue);
            if (value is not int original || original == 0) return null;

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(GestureBackupFile)!);
                File.WriteAllText(
                    GestureBackupFile,
                    original.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke($"Could not back up Windows 3-finger gesture: {ex.Message}");
                return null;
            }

            key.SetValue(ThreeFingerSlideValue, 0, RegistryValueKind.DWord);
            StatusChanged?.Invoke(
                $"Windows 3-finger slide disabled (was 0x{original:X8}); restored on exit.");
            return original;
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke($"Could not disable Windows 3-finger gestures: {ex.Message}");
            return null;
        }
    }

    private void RestoreWindowsThreeFingerSlide()
    {
        if (_originalThreeFingerSlide is null) return;

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PrecisionTouchpadKey, writable: true);
            key?.SetValue(ThreeFingerSlideValue, _originalThreeFingerSlide.Value, RegistryValueKind.DWord);
            try { File.Delete(GestureBackupFile); } catch { }
            StatusChanged?.Invoke(
                $"Windows 3-finger slide restored to 0x{_originalThreeFingerSlide.Value:X8}.");
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke($"Could not restore Windows 3-finger gestures: {ex.Message}");
        }
        finally
        {
            _originalThreeFingerSlide = null;
        }
    }

    private void RecoverStaleGestureBackup()
    {
        try
        {
            if (!File.Exists(GestureBackupFile)) return;

            string text = File.ReadAllText(GestureBackupFile).Trim();
            if (!int.TryParse(
                    text,
                    System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out int original))
            {
                File.Delete(GestureBackupFile);
                return;
            }

            using var key = Registry.CurrentUser.OpenSubKey(PrecisionTouchpadKey, writable: true);
            if (key is not null && key.GetValue(ThreeFingerSlideValue) is int current && current == 0)
            {
                key.SetValue(ThreeFingerSlideValue, original, RegistryValueKind.DWord);
                StatusChanged?.Invoke(
                    $"Recovered Windows 3-finger slide from a previous crash (0x{original:X8}).");
            }

            File.Delete(GestureBackupFile);
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke($"Could not recover Windows 3-finger gesture: {ex.Message}");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;

        lock (_gate)
        {
            if (_enabled)
            {
                Execute(_fsm.Reset("dispose"));
                _source.Stop();
                _enabled = false;
            }

            RestoreWindowsThreeFingerSlide();
            _disposed = true;
        }

        _watchdog.Dispose();
        _source.Dispose();
    }
}
