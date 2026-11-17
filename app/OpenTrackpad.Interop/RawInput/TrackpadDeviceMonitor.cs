using OpenTrackpad.Interop.Driver;

namespace OpenTrackpad.Interop.RawInput;

public sealed record TrackpadDeviceChange(
    IReadOnlyList<TrackpadDevice> Devices,
    TrackpadDevice? Preferred)
{
    public TrackpadTransport Transport => Preferred?.Transport ?? TrackpadTransport.None;
    public bool IsConnected => Preferred is not null;
}

/// <summary>
/// Polls the Raw Input device list and raises <see cref="Changed"/> when the
/// set of Magic Trackpad 2 PTP collections changes. Polling is used instead of
/// WM_DEVICECHANGE so the tray app does not need a window handle and missed
/// broadcasts cannot leave the status stale.
/// </summary>
public sealed class TrackpadDeviceMonitor : IDisposable
{
    private readonly Func<IReadOnlyList<TrackpadDevice>> _probe;
    private readonly TimeSpan _interval;
    private readonly Timer _timer;
    private readonly object _gate = new();

    private string? _signature;
    private bool _started;
    private bool _disposed;

    public TrackpadDeviceMonitor(
        Func<IReadOnlyList<TrackpadDevice>>? probe = null,
        TimeSpan? interval = null)
    {
        _probe = probe ?? TrackpadDeviceLocator.FindAll;
        _interval = interval ?? TimeSpan.FromSeconds(1);
        _timer = new Timer(_ => Poll(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public event Action<TrackpadDeviceChange>? Changed;
    public event Action<Exception>? Error;

    public TrackpadDeviceChange Current { get; private set; } =
        new(Array.Empty<TrackpadDevice>(), null);

    public void Start()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_started) return;
            _started = true;
        }

        Poll();

        lock (_gate)
        {
            if (_started && !_disposed)
                _timer.Change(_interval, _interval);
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            _started = false;
            _timer.Change(Timeout.Infinite, Timeout.Infinite);
        }
    }

    public void Poll()
    {
        TrackpadDeviceChange change;
        try
        {
            change = CreateChange(_probe());
        }
        catch (Exception ex)
        {
            Error?.Invoke(ex);
            return;
        }

        lock (_gate)
        {
            if (_disposed) return;

            string signature = Signature(change);
            if (signature == _signature) return;

            _signature = signature;
            Current = change;
        }

        try
        {
            Changed?.Invoke(change);
        }
        catch (Exception ex)
        {
            Error?.Invoke(ex);
        }
    }

    private static TrackpadDeviceChange CreateChange(IReadOnlyList<TrackpadDevice> devices)
    {
        List<TrackpadDevice> ordered = devices
            .OrderBy(device => device.Transport == TrackpadTransport.Usb ? 0 : 1)
            .ThenBy(device => device.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new TrackpadDeviceChange(ordered, ordered.FirstOrDefault());
    }

    private static string Signature(TrackpadDeviceChange change) =>
        string.Join("|", change.Devices.Select(
            device => $"{device.Transport}:{device.Path}"));

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _started = false;
        }

        _timer.Dispose();
    }
}
