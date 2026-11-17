using OpenTrackpad.Core.Battery;
using OpenTrackpad.Interop.Driver;

namespace OpenTrackpad.App;

public sealed class BatteryMonitor : IDisposable
{
    private readonly TrackpadBatteryReader _reader;
    private readonly BatteryAlertPolicy _alerts = new();
    private readonly System.Threading.Timer _timer;
    private readonly object _gate = new();

    private int _lastLevel = -1;
    private bool _disposed;

    public BatteryMonitor(TrackpadBatteryReader reader)
    {
        _reader = reader;
        _timer = new System.Threading.Timer(_ => Refresh(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public event Action<BatteryReading>? LevelChanged;
    public event Action<BatteryAlertKind>? AlertRaised;
    public event Action<string>? Error;

    public int LastLevel
    {
        get { lock (_gate) return _lastLevel; }
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
            BatteryReading reading = _reader.Read();
            lock (_gate) _lastLevel = reading.Level;
            LevelChanged?.Invoke(reading);

            var alert = _alerts.Evaluate(reading.Level, DateTimeOffset.Now);
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
