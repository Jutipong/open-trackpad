namespace OpenTrackpad.Core.Battery;

public enum BatteryAlertKind
{
    Low,
    Critical,
}

public sealed class BatteryAlertPolicy
{
    private DateTimeOffset? _lastLow;
    private DateTimeOffset? _lastCritical;
    private bool _lowActive;
    private bool _criticalActive;

    public int LowThreshold { get; init; } = 20;
    public int CriticalThreshold { get; init; } = 10;
    public TimeSpan Cooldown { get; init; } = TimeSpan.FromHours(1);

    public BatteryAlertKind? Evaluate(int level, DateTimeOffset now)
    {
        if (level is < 0 or > 100)
            throw new ArgumentOutOfRangeException(nameof(level), "Battery level must be 0..100.");
        if (CriticalThreshold > LowThreshold)
            throw new InvalidOperationException("Critical threshold must not exceed low threshold.");
        if (Cooldown < TimeSpan.Zero)
            throw new InvalidOperationException("Cooldown must not be negative.");

        // Hysteresis: a rise above the thresholds re-arms the next alert.
        if (level > LowThreshold + 1)
        {
            _lowActive = false;
            _criticalActive = false;
        }
        else if (level > CriticalThreshold + 1)
        {
            _criticalActive = false;
        }

        if (level <= CriticalThreshold &&
            !_criticalActive &&
            CooldownElapsed(_lastCritical, now))
        {
            _criticalActive = true;
            _lowActive = true;
            _lastCritical = now;
            _lastLow = now;
            return BatteryAlertKind.Critical;
        }

        if (level <= LowThreshold &&
            !_lowActive &&
            CooldownElapsed(_lastLow, now))
        {
            _lowActive = true;
            _lastLow = now;
            return BatteryAlertKind.Low;
        }

        return null;
    }

    private bool CooldownElapsed(DateTimeOffset? last, DateTimeOffset now) =>
        last is null || now - last.Value >= Cooldown;
}
