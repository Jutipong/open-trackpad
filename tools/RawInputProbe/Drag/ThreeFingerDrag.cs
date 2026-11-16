using Microsoft.Win32;
using RawInputProbe.Input;
using RawInputProbe.Ptp;

namespace RawInputProbe.Drag;

public sealed class ThreeFingerDrag : IDisposable
{
    private const int RequiredContacts = 3;
    private const string PrecisionTouchpadKey =
        @"Software\Microsoft\Windows\CurrentVersion\PrecisionTouchPad";

    private readonly object _gate = new();
    private readonly Timer _watchdog;
    private readonly double _gain;
    private readonly double _startThreshold;
    private readonly int _stableMs;
    private readonly int? _originalThreeFingerSlide;

    private DateTime _lastFrameUtc = DateTime.UtcNow;
    private DateTime? _stableSince;
    private string _stableKey = "";
    private double _startX;
    private double _startY;
    private double _lastX;
    private double _lastY;
    private double _residualX;
    private double _residualY;
    private bool _dragging;
    private bool _disposed;

    public event Action<string>? Status;

    public ThreeFingerDrag(
        double gain = 0.5,
        double startThreshold = 25,
        int stableMs = 40,
        bool disableThreeFingerGestures = true)
    {
        _gain = gain;
        _startThreshold = startThreshold;
        _stableMs = stableMs;

        if (disableThreeFingerGestures)
            _originalThreeFingerSlide = DisableThreeFingerGesture();

        _watchdog = new Timer(_ => Watchdog(), null, 100, 100);
    }

    public bool IsDragging
    {
        get { lock (_gate) return _dragging; }
    }

    public void Process(PtpFrame frame)
    {
        lock (_gate)
        {
            if (_disposed) return;
            _lastFrameUtc = DateTime.UtcNow;

            var active = frame.Contacts
                .Where(c => c.Tip != 0 && c.Confidence != 0)
                .OrderBy(c => c.Id)
                .ToList();

            if (active.Count != RequiredContacts)
            {
                ResetLocked($"contact count {active.Count}");
                return;
            }

            string key = string.Join(",", active.Select(c => c.Id));
            double x = active.Average(c => (double)c.X);
            double y = active.Average(c => (double)c.Y);

            if (_stableSince is null || key != _stableKey)
            {
                if (_dragging)
                    ReleaseLocked("contact set changed");
                _stableSince = DateTime.UtcNow;
                _stableKey = key;
                _startX = x;
                _startY = y;
                return;
            }

            if (!_dragging)
            {
                if ((DateTime.UtcNow - _stableSince.Value).TotalMilliseconds < _stableMs)
                    return;

                double moved = Math.Sqrt(
                    (x - _startX) * (x - _startX) + (y - _startY) * (y - _startY));
                if (moved < _startThreshold)
                    return;

                if (MouseInjector.IsLeftButtonDown())
                {
                    ResetLocked("left button already down");
                    return;
                }

                try
                {
                    MouseInjector.LeftDown();
                }
                catch (Exception ex)
                {
                    Status?.Invoke($"LeftDown failed: {ex.Message}");
                    ResetLocked("left down failed");
                    return;
                }

                _dragging = true;
                _lastX = x;
                _lastY = y;
                _residualX = 0;
                _residualY = 0;
                Status?.Invoke($"DRAG START ids={key} x={x:0} y={y:0}");
                return;
            }

            double dx = (x - _lastX) * _gain + _residualX;
            double dy = (y - _lastY) * _gain + _residualY;
            _lastX = x;
            _lastY = y;

            int ix = (int)Math.Round(dx);
            int iy = (int)Math.Round(dy);
            _residualX = dx - ix;
            _residualY = dy - iy;

            if (ix != 0 || iy != 0)
            {
                try
                {
                    MouseInjector.Move(ix, iy);
                }
                catch (Exception ex)
                {
                    Status?.Invoke($"Move failed: {ex.Message}");
                    ReleaseLocked("move failed");
                }
            }
        }
    }

    public void Release(string reason = "requested")
    {
        lock (_gate)
        {
            ReleaseLocked(reason);
        }
    }

    private void Watchdog()
    {
        lock (_gate)
        {
            if (_disposed) return;
            if (_dragging && (DateTime.UtcNow - _lastFrameUtc).TotalMilliseconds > 500)
                ReleaseLocked("no frames for 500 ms");
        }
    }

    private void ResetLocked(string reason)
    {
        if (_dragging)
            ReleaseLocked(reason);
        _stableSince = null;
        _stableKey = "";
        _residualX = 0;
        _residualY = 0;
    }

    private void ReleaseLocked(string reason)
    {
        if (!_dragging)
        {
            _stableSince = null;
            _stableKey = "";
            return;
        }

        // Fail-open: always attempt the left-up injection.
        try
        {
            MouseInjector.LeftUp();
        }
        catch (Exception ex)
        {
            Status?.Invoke($"LeftUp failed: {ex.Message}");
        }

        _dragging = false;
        _stableSince = null;
        _stableKey = "";
        _residualX = 0;
        _residualY = 0;
        Status?.Invoke($"DRAG END {reason}");
    }

    private int? DisableThreeFingerGesture()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PrecisionTouchpadKey, writable: true);
            if (key is null)
                return null;

            object? value = key.GetValue("ThreeFingerSlideEnabled");
            if (value is not int original || original == 0)
                return null;

            key.SetValue("ThreeFingerSlideEnabled", 0, RegistryValueKind.DWord);
            Status?.Invoke(
                $"Windows 3-finger slide disabled (was 0x{original:X8}); restored on exit.");
            return original;
        }
        catch (Exception ex)
        {
            Status?.Invoke($"Could not disable Windows 3-finger gestures: {ex.Message}");
            return null;
        }
    }

    private void RestoreThreeFingerGesture()
    {
        if (_originalThreeFingerSlide is null)
            return;

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PrecisionTouchpadKey, writable: true);
            key?.SetValue("ThreeFingerSlideEnabled", _originalThreeFingerSlide.Value, RegistryValueKind.DWord);
            Status?.Invoke(
                $"Windows 3-finger slide restored to 0x{_originalThreeFingerSlide.Value:X8}.");
        }
        catch (Exception ex)
        {
            Status?.Invoke($"Could not restore Windows 3-finger gestures: {ex.Message}");
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            ReleaseLocked("dispose");
        }

        _watchdog.Dispose();
        RestoreThreeFingerGesture();
    }
}
