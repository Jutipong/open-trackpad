namespace OpenTrackpad.Core.Input;

public sealed class ThreeFingerDragOptions
{
    public int RequiredContacts { get; init; } = 3;
    public int StableMs { get; init; } = 40;
    public double StartThreshold { get; init; } = 25;
    public double Gain { get; init; } = 0.5;
    public int StaleFrameMs { get; init; } = 500;

    /// <summary>
    /// Keep the drag alive while a tracked contact disappears for a few frames
    /// (palm brush, sensor dropout, contact-id churn) before releasing the button.
    /// </summary>
    public int LiftGraceMs { get; init; } = 60;

    /// <summary>
    /// Cursor speed below which movement is treated as hand tremor and discarded.
    /// </summary>
    public double MinSpeedPxPerMs { get; init; } = 0.020;

    /// <summary>
    /// Cursor speed required to leave the tremor-freeze state.
    /// </summary>
    public double ResumeSpeedPxPerMs { get; init; } = 0.040;

    /// <summary>
    /// Freeze movement when the average pressure falls below this fraction of the
    /// drag baseline (prevents the final lift frames from moving the cursor).
    /// </summary>
    public double PressureFreezeRatio { get; init; } = 0.25;
}

/// <summary>
/// Pure 3-finger drag state machine. It does not inject input; it emits
/// Start/Move/End actions for the interop layer to execute.
/// </summary>
public sealed class ThreeFingerDragFsm
{
    private readonly ThreeFingerDragOptions _options;
    private readonly int[] _stableIds;
    private readonly int[] _trackedIds;

    private long? _stableSinceMs;
    private bool _hasStableIds;
    private int _stableCount;
    private double _startX;
    private double _startY;
    private double _lastX;
    private double _lastY;
    private double _residualX;
    private double _residualY;
    private long _lastFrameMs;
    private int _lastScanTime;
    private bool _dragging;
    private bool _moving;
    private long? _liftSinceMs;
    private int _trackedCount;
    private double _pressureBaseline;
    private bool _hasPressureBaseline;
    private double _gain;

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
        if (_options.LiftGraceMs < 0)
            throw new ArgumentOutOfRangeException(nameof(options), "LiftGraceMs must not be negative.");
        if (_options.MinSpeedPxPerMs < 0)
            throw new ArgumentOutOfRangeException(nameof(options), "MinSpeedPxPerMs must not be negative.");
        if (_options.ResumeSpeedPxPerMs < _options.MinSpeedPxPerMs)
            throw new ArgumentOutOfRangeException(
                nameof(options), "ResumeSpeedPxPerMs must be >= MinSpeedPxPerMs.");
        if (_options.PressureFreezeRatio is < 0 or >= 1)
            throw new ArgumentOutOfRangeException(
                nameof(options), "PressureFreezeRatio must be in [0, 1).");

        _gain = _options.Gain;
        _stableIds = new int[_options.RequiredContacts];
        _trackedIds = new int[_options.RequiredContacts];
    }

    public bool IsDragging => _dragging;

    /// <summary>
    /// Finger-to-cursor gain. Can be changed at runtime (UI speed slider).
    /// </summary>
    public double Gain
    {
        get => _gain;
        set
        {
            if (value <= 0)
                throw new ArgumentOutOfRangeException(nameof(value), "Gain must be positive.");
            _gain = value;
        }
    }

    public IReadOnlyList<DragAction> Process(TouchFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        long previousFrameMs = _lastFrameMs;
        _lastFrameMs = frame.TimestampMs;

        return _dragging
            ? ProcessDrag(frame, previousFrameMs)
            : ProcessIdle(frame, allowStart: true);
    }

    /// <summary>
    /// Keep tracking the touch sequence without starting a drag, for example
    /// while a fast 3-finger swipe is still possible. Unlike <see cref="Reset"/>
    /// this keeps the stable contact set and start point, so the drag can start
    /// on the first frame after the hold instead of waiting for StableMs again.
    /// </summary>
    public void Hold(TouchFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (_dragging) return;

        _lastFrameMs = frame.TimestampMs;
        ProcessIdle(frame, allowStart: false);
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
        _hasStableIds = false;
        _stableCount = 0;
        _trackedCount = 0;
        _liftSinceMs = null;
        _moving = false;
        _residualX = 0;
        _residualY = 0;
        _lastScanTime = 0;
        _hasPressureBaseline = false;
        _pressureBaseline = 0;

        return wasDragging
            ? new[] { new DragAction(DragActionKind.End, Reason: reason) }
            : Array.Empty<DragAction>();
    }

    private IReadOnlyList<DragAction> ProcessIdle(TouchFrame frame, bool allowStart)
    {
        var active = new List<TouchContact>(_options.RequiredContacts);
        foreach (var contact in frame.Contacts)
        {
            if (contact.IsActive)
                active.Add(contact);
        }

        if (active.Count != _options.RequiredContacts)
            return Reset(active.Count == 0 ? "all contacts lifted" : "contact count changed");

        active.Sort(static (a, b) => a.Id.CompareTo(b.Id));

        double x = 0;
        double y = 0;
        for (int i = 0; i < active.Count; i++)
        {
            x += active[i].X;
            y += active[i].Y;
        }
        x /= active.Count;
        y /= active.Count;

        if (!_hasStableIds || !SameStableIds(active))
        {
            _hasStableIds = true;
            _stableCount = active.Count;
            for (int i = 0; i < active.Count; i++)
                _stableIds[i] = active[i].Id;

            _stableSinceMs = frame.TimestampMs;
            _startX = x;
            _startY = y;
            _residualX = 0;
            _residualY = 0;
            return Array.Empty<DragAction>();
        }

        if (frame.TimestampMs - _stableSinceMs!.Value < _options.StableMs)
            return Array.Empty<DragAction>();

        double moved = Math.Sqrt(
            (x - _startX) * (x - _startX) + (y - _startY) * (y - _startY));
        if (moved < _options.StartThreshold || !allowStart)
            return Array.Empty<DragAction>();

        _dragging = true;
        _trackedCount = active.Count;
        for (int i = 0; i < active.Count; i++)
            _trackedIds[i] = active[i].Id;

        _lastX = x;
        _lastY = y;
        _lastScanTime = frame.ScanTime;
        _lastFrameMs = frame.TimestampMs;
        _residualX = 0;
        _residualY = 0;
        _liftSinceMs = null;
        _moving = true;
        _pressureBaseline = AveragePressure(active);
        _hasPressureBaseline = _pressureBaseline > 0;

        return new[] { new DragAction(DragActionKind.Start) };
    }

    private IReadOnlyList<DragAction> ProcessDrag(TouchFrame frame, long previousFrameMs)
    {
        double sumX = 0;
        double sumY = 0;
        double pressure = 0;
        int found = 0;

        for (int tracked = 0; tracked < _trackedCount; tracked++)
        {
            foreach (var contact in frame.Contacts)
            {
                if (contact.Id != _trackedIds[tracked] || !contact.IsActive)
                    continue;

                sumX += contact.X;
                sumY += contact.Y;
                pressure += contact.Pressure;
                found++;
                break;
            }
        }

        if (found < _trackedCount)
        {
            _liftSinceMs ??= frame.TimestampMs;
            if (frame.TimestampMs - _liftSinceMs.Value >= _options.LiftGraceMs)
                return Reset("contact lifted");

            // Keep the button down and the cursor still while waiting for the
            // contact to return. This bridges palm brushes and sensor dropouts.
            return Array.Empty<DragAction>();
        }

        bool resumedAfterLift = _liftSinceMs is not null;
        _liftSinceMs = null;

        double x = sumX / _trackedCount;
        double y = sumY / _trackedCount;

        if (resumedAfterLift)
        {
            // Re-anchor so the gap does not become a cursor jump.
            _lastX = x;
            _lastY = y;
            _residualX = 0;
            _residualY = 0;
            _moving = false;
            _lastScanTime = frame.ScanTime;
            return Array.Empty<DragAction>();
        }

        double averagePressure = pressure / _trackedCount;
        if (!_hasPressureBaseline)
        {
            _pressureBaseline = averagePressure;
            _hasPressureBaseline = averagePressure > 0;
        }
        else
        {
            _pressureBaseline += (averagePressure - _pressureBaseline) * 0.05;
        }

        if (_hasPressureBaseline && _pressureBaseline >= 3 &&
            averagePressure <= Math.Max(1, _pressureBaseline * _options.PressureFreezeRatio))
        {
            // Fingers are releasing; hold position instead of following the
            // contact shrink to the lift point.
            _lastX = x;
            _lastY = y;
            _residualX = 0;
            _residualY = 0;
            return Array.Empty<DragAction>();
        }

        double dtMs = GetFrameDeltaMs(frame, previousFrameMs);
        if (dtMs <= 0)
            return Array.Empty<DragAction>();

        dtMs = Math.Clamp(dtMs, 1, 40);

        double rawDx = x - _lastX;
        double rawDy = y - _lastY;
        _lastX = x;
        _lastY = y;

        double speedPxPerMs =
            Math.Sqrt(rawDx * rawDx + rawDy * rawDy) * _gain / dtMs;

        if (_moving)
        {
            if (speedPxPerMs < _options.MinSpeedPxPerMs)
            {
                _moving = false;
                _residualX = 0;
                _residualY = 0;
                return Array.Empty<DragAction>();
            }
        }
        else
        {
            if (speedPxPerMs < _options.ResumeSpeedPxPerMs)
            {
                _residualX = 0;
                _residualY = 0;
                return Array.Empty<DragAction>();
            }

            _moving = true;
        }

        double dx = rawDx * _gain + _residualX;
        double dy = rawDy * _gain + _residualY;

        int ix = (int)Math.Round(dx);
        int iy = (int)Math.Round(dy);
        _residualX = dx - ix;
        _residualY = dy - iy;

        if (ix == 0 && iy == 0)
            return Array.Empty<DragAction>();

        return new[] { new DragAction(DragActionKind.Move, ix, iy) };
    }

    private double GetFrameDeltaMs(TouchFrame frame, long previousFrameMs)
    {
        if (frame.ScanTime > 0 && _lastScanTime > 0)
        {
            // Scan time is a 16-bit 10 kHz counter: 10 ticks per millisecond.
            int deltaTicks = (ushort)(frame.ScanTime - _lastScanTime);
            if (deltaTicks == 0)
                return 0; // duplicate sample delivered by a burst

            _lastScanTime = frame.ScanTime;
            return deltaTicks / 10.0;
        }

        if (frame.ScanTime > 0)
            _lastScanTime = frame.ScanTime;

        if (previousFrameMs <= 0)
            return 40;

        return frame.TimestampMs - previousFrameMs;
    }

    private bool SameStableIds(List<TouchContact> active)
    {
        if (!_hasStableIds || active.Count != _stableCount)
            return false;

        for (int i = 0; i < active.Count; i++)
        {
            if (active[i].Id != _stableIds[i])
                return false;
        }

        return true;
    }

    private static double AveragePressure(List<TouchContact> contacts)
    {
        double total = 0;
        foreach (var contact in contacts)
            total += contact.Pressure;
        return total / contacts.Count;
    }
}
