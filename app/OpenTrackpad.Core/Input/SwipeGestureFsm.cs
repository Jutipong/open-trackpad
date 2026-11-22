namespace OpenTrackpad.Core.Input;

public enum SwipeDirection
{
    Left,
    Right,
    Up,
    Down,
}

public readonly record struct SwipeGesture(SwipeDirection Direction, int ContactCount);

public sealed class SwipeGestureOptions
{
    public int RequiredContacts { get; init; } = 4;

    /// <summary>
    /// Contacts must stay on the same ids for this long before a swipe can start.
    /// </summary>
    public int StableMs { get; init; } = 40;

    /// <summary>
    /// A swipe must complete within this time. Slow drags never qualify.
    /// </summary>
    public int MaxGestureMs { get; init; } = 600;

    /// <summary>
    /// Minimum travel in trackpad units before the gesture is emitted.
    /// About 60 units per millimetre on the Magic Trackpad 2.
    /// </summary>
    public double StartThreshold { get; init; } = 60;

    /// <summary>
    /// Earlier travel that marks a possible fast horizontal swipe. The drag
    /// state machine is paused while this is true so a swipe does not move a
    /// window before switching desktops.
    /// </summary>
    public double CandidateThreshold { get; init; } = 12;

    /// <summary>
    /// How much the dominant axis must exceed the other axis.
    /// </summary>
    public double AxisRatio { get; init; } = 1.5;

    /// <summary>
    /// Minimum smoothed speed in trackpad units per millisecond.
    /// </summary>
    public double MinSpeedPxPerMs { get; init; } = 0.25;

    /// <summary>
    /// Three-finger gestures only use horizontal swipes (drag owns the rest).
    /// </summary>
    public bool HorizontalOnly { get; init; }
}

/// <summary>
/// Pure multi-finger swipe state machine. It emits at most one gesture per
/// touch sequence and ignores slow movement so it can coexist with 3-finger
/// drag.
/// </summary>
public sealed class SwipeGestureFsm
{
    private readonly SwipeGestureOptions _options;
    private readonly int[] _ids;

    private bool _tracking;
    private bool _locked;
    private int _idCount;
    private long _startMs;
    private long _lastMs;
    private int _lastScanTime;
    private double _startX;
    private double _startY;
    private double _lastX;
    private double _lastY;
    private double _speed;

    public SwipeGestureFsm(SwipeGestureOptions? options = null)
    {
        _options = options ?? new SwipeGestureOptions();
        if (_options.RequiredContacts < 1)
            throw new ArgumentOutOfRangeException(nameof(options), "RequiredContacts must be positive.");
        if (_options.StableMs < 0)
            throw new ArgumentOutOfRangeException(nameof(options), "StableMs must not be negative.");
        if (_options.MaxGestureMs < _options.StableMs)
            throw new ArgumentOutOfRangeException(
                nameof(options), "MaxGestureMs must be >= StableMs.");
        if (_options.StartThreshold <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), "StartThreshold must be positive.");
        if (_options.CandidateThreshold < 0)
            throw new ArgumentOutOfRangeException(
                nameof(options), "CandidateThreshold must not be negative.");
        if (_options.AxisRatio <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), "AxisRatio must be positive.");
        if (_options.MinSpeedPxPerMs < 0)
            throw new ArgumentOutOfRangeException(
                nameof(options), "MinSpeedPxPerMs must not be negative.");

        _ids = new int[_options.RequiredContacts];
    }

    /// <summary>
    /// True while a fast swipe is still possible and the drag state machine
    /// should wait instead of starting a drag.
    /// </summary>
    public bool IsCandidate { get; private set; }

    public SwipeGesture? Process(TouchFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        IsCandidate = false;

        var active = new List<TouchContact>(_options.RequiredContacts);
        foreach (var contact in frame.Contacts)
        {
            if (contact.IsActive)
                active.Add(contact);
        }

        if (active.Count != _options.RequiredContacts)
        {
            ResetTracking();
            return null;
        }

        active.Sort(static (a, b) => a.Id.CompareTo(b.Id));

        if (!_tracking || !SameIds(active))
        {
            Begin(active, frame.TimestampMs, frame.ScanTime);
            return null;
        }

        long elapsed = frame.TimestampMs - _startMs;
        if (elapsed < _options.StableMs)
            return null;

        double x = 0;
        double y = 0;
        for (int i = 0; i < active.Count; i++)
        {
            x += active[i].X;
            y += active[i].Y;
        }
        x /= active.Count;
        y /= active.Count;

        double dt = GetDeltaMs(frame, frame.TimestampMs - _lastMs);
        if (dt > 0)
        {
            double step = Math.Sqrt(
                (x - _lastX) * (x - _lastX) + (y - _lastY) * (y - _lastY));
            double instant = step / dt;
            _speed = _speed <= 0 ? instant : _speed * 0.5 + instant * 0.5;
            _lastMs = frame.TimestampMs;
            _lastX = x;
            _lastY = y;
        }

        if (_locked)
            return null;

        if (elapsed > _options.MaxGestureMs)
        {
            _locked = true;
            return null;
        }

        double dx = x - _startX;
        double dy = y - _startY;
        double distance = Math.Sqrt(dx * dx + dy * dy);
        bool horizontal = Math.Abs(dx) >= Math.Abs(dy) * _options.AxisRatio;
        bool vertical = Math.Abs(dy) >= Math.Abs(dx) * _options.AxisRatio;

        bool candidateAxis = _options.HorizontalOnly ? horizontal : horizontal || vertical;
        IsCandidate =
            candidateAxis &&
            distance >= _options.CandidateThreshold &&
            _speed >= _options.MinSpeedPxPerMs;

        if (distance < _options.StartThreshold || _speed < _options.MinSpeedPxPerMs)
            return null;

        if (!candidateAxis)
            return null;

        _locked = true;
        IsCandidate = false;

        SwipeDirection direction = horizontal
            ? (dx < 0 ? SwipeDirection.Left : SwipeDirection.Right)
            : (dy < 0 ? SwipeDirection.Up : SwipeDirection.Down);

        return new SwipeGesture(direction, _options.RequiredContacts);
    }

    /// <summary>
    /// Prevent any further gesture until every required contact lifts.
    /// Used when 3-finger drag takes over a sequence.
    /// </summary>
    public void LockUntilLift()
    {
        _locked = true;
        IsCandidate = false;
    }

    public void Reset()
    {
        ResetTracking();
    }

    private void Begin(List<TouchContact> active, long timestampMs, int scanTime)
    {
        _tracking = true;
        _idCount = active.Count;
        _startMs = timestampMs;
        _lastMs = timestampMs;
        _lastScanTime = scanTime;
        _speed = 0;
        _startX = 0;
        _startY = 0;

        for (int i = 0; i < active.Count; i++)
        {
            _ids[i] = active[i].Id;
            _startX += active[i].X;
            _startY += active[i].Y;
        }

        _startX /= active.Count;
        _startY /= active.Count;
        _lastX = _startX;
        _lastY = _startY;
    }

    private double GetDeltaMs(TouchFrame frame, long fallbackDeltaMs)
    {
        if (frame.ScanTime > 0 && _lastScanTime > 0)
        {
            // Scan time is a 16-bit 10 kHz counter: 10 ticks per millisecond.
            int deltaTicks = (ushort)(frame.ScanTime - _lastScanTime);
            if (deltaTicks == 0)
                return 0;

            _lastScanTime = frame.ScanTime;
            return deltaTicks / 10.0;
        }

        if (frame.ScanTime > 0)
            _lastScanTime = frame.ScanTime;

        return fallbackDeltaMs;
    }

    private void ResetTracking()
    {
        _tracking = false;
        _locked = false;
        _idCount = 0;
        _startMs = 0;
        _lastMs = 0;
        _lastScanTime = 0;
        _speed = 0;
        IsCandidate = false;
    }

    private bool SameIds(List<TouchContact> active)
    {
        if (active.Count != _idCount)
            return false;

        for (int i = 0; i < active.Count; i++)
        {
            if (active[i].Id != _ids[i])
                return false;
        }

        return true;
    }
}
