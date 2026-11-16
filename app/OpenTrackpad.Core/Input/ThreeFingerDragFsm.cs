namespace OpenTrackpad.Core.Input;

public sealed class ThreeFingerDragOptions
{
    public int RequiredContacts { get; init; } = 3;
    public int StableMs { get; init; } = 40;
    public double StartThreshold { get; init; } = 25;
    public double Gain { get; init; } = 0.5;
    public int StaleFrameMs { get; init; } = 500;
}

/// <summary>
/// Pure 3-finger drag state machine. It does not inject input; it emits
/// Start/Move/End actions for the interop layer to execute.
/// </summary>
public sealed class ThreeFingerDragFsm
{
    private readonly ThreeFingerDragOptions _options;

    private long? _stableSinceMs;
    private string _stableKey = string.Empty;
    private double _startX;
    private double _startY;
    private double _lastX;
    private double _lastY;
    private double _residualX;
    private double _residualY;
    private long _lastFrameMs;
    private bool _dragging;

    public ThreeFingerDragFsm(ThreeFingerDragOptions? options = null)
    {
        _options = options ?? new ThreeFingerDragOptions();
        if (_options.RequiredContacts < 1)
            throw new ArgumentOutOfRangeException(nameof(options), "RequiredContacts must be positive.");
        if (_options.StableMs < 0)
            throw new ArgumentOutOfRangeException(nameof(options), "StableMs must not be negative.");
        if (_options.StartThreshold < 0)
            throw new ArgumentOutOfRangeException(nameof(options), "StartThreshold must not be negative.");
        if (_options.Gain <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), "Gain must be positive.");
        if (_options.StaleFrameMs < 0)
            throw new ArgumentOutOfRangeException(nameof(options), "StaleFrameMs must not be negative.");
    }

    public bool IsDragging => _dragging;

    public IReadOnlyList<DragAction> Process(TouchFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        _lastFrameMs = frame.TimestampMs;

        var active = frame.Contacts
            .Where(c => c.IsActive)
            .OrderBy(c => c.Id)
            .ToList();

        if (active.Count != _options.RequiredContacts)
            return Reset(active.Count == 0 ? "all contacts lifted" : "contact count changed");

        string key = string.Join(",", active.Select(c => c.Id));
        double x = active.Average(c => (double)c.X);
        double y = active.Average(c => (double)c.Y);

        if (_stableSinceMs is null || key != _stableKey)
        {
            var actions = new List<DragAction>();
            if (_dragging)
                actions.Add(new DragAction(DragActionKind.End, Reason: "contact set changed"));

            _dragging = false;
            _stableSinceMs = frame.TimestampMs;
            _stableKey = key;
            _startX = x;
            _startY = y;
            _residualX = 0;
            _residualY = 0;
            return actions;
        }

        if (!_dragging)
        {
            if (frame.TimestampMs - _stableSinceMs.Value < _options.StableMs)
                return Array.Empty<DragAction>();

            double moved = Math.Sqrt(
                (x - _startX) * (x - _startX) + (y - _startY) * (y - _startY));
            if (moved < _options.StartThreshold)
                return Array.Empty<DragAction>();

            _dragging = true;
            _lastX = x;
            _lastY = y;
            _residualX = 0;
            _residualY = 0;
            return new[] { new DragAction(DragActionKind.Start) };
        }

        double dx = (x - _lastX) * _options.Gain + _residualX;
        double dy = (y - _lastY) * _options.Gain + _residualY;
        _lastX = x;
        _lastY = y;

        int ix = (int)Math.Round(dx);
        int iy = (int)Math.Round(dy);
        _residualX = dx - ix;
        _residualY = dy - iy;

        if (ix == 0 && iy == 0)
            return Array.Empty<DragAction>();

        return new[] { new DragAction(DragActionKind.Move, ix, iy) };
    }

    /// <summary>
    /// Watchdog for a stalled device. Call periodically; emits End when no
    /// frames have arrived for StaleFrameMs while dragging.
    /// </summary>
    public IReadOnlyList<DragAction> Tick(long nowMs)
    {
        if (!_dragging)
            return Array.Empty<DragAction>();

        if (nowMs - _lastFrameMs < _options.StaleFrameMs)
            return Array.Empty<DragAction>();

        return Reset("no frames");
    }

    /// <summary>
    /// Force release (mode off, Ctrl+C, device disconnect, app exit).
    /// </summary>
    public IReadOnlyList<DragAction> Reset(string reason)
    {
        bool wasDragging = _dragging;
        _dragging = false;
        _stableSinceMs = null;
        _stableKey = string.Empty;
        _residualX = 0;
        _residualY = 0;

        return wasDragging
            ? new[] { new DragAction(DragActionKind.End, Reason: reason) }
            : Array.Empty<DragAction>();
    }
}
