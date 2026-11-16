using OpenTrackpad.Core.Battery;
using OpenTrackpad.Interop.Driver;

namespace OpenTrackpad.App;

public sealed class BatteryMonitor : IDisposable
{
    private readonly IControlPanelPipe _pipe;
    private readonly BatteryAlertPolicy _alerts = new();
    private readonly System.Threading.Timer _timer;

    private int _lastLevel = -1;
    private bool _disposed;

    public BatteryMonitor(IControlPanelPipe pipe)
    {
        _pipe = pipe;
        _timer = new System.Threading.Timer(_ => Refresh(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public event Action<int>? LevelChanged;
    public event Action<BatteryAlertKind>? AlertRaised;
    public event Action<string>? Error;

    public int LastLevel
    {
        get { lock (this) return _lastLevel; }
    }

    public void Start()
    {
        _timer.Change(TimeSpan.Zero, TimeSpan.FromSeconds(60));
    }

    public void Stop() => _timer.Change(Timeout.Infinite, Timeout.Infinite);

    public void Refresh()
    {
        try
        {
            var response = _pipe.GetBattery();
            if (!response.Ok)
            {
                Error?.Invoke($"ไดรเวอร์ตอบสถานะ 0x{response.Status:X2}");
                return;
            }

            int level = response.Value;
            lock (this) _lastLevel = level;
            LevelChanged?.Invoke(level);

            var alert = _alerts.Evaluate(level, DateTimeOffset.Now);
            if (alert.HasValue) AlertRaised?.Invoke(alert.Value);
        }
        catch (Exception ex)
        {
            Error?.Invoke(ex.Message);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _timer.Dispose();
    }
}
