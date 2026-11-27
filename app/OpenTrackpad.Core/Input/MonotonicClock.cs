using System.Diagnostics;

namespace OpenTrackpad.Core.Input;

/// <summary>
/// Millisecond clock for touch frames and gesture timeouts.
/// <see cref="Environment.TickCount64"/> only advances every 10-16 ms, which is
/// coarser than the ~11 ms trackpad report interval; this uses the
/// high-resolution performance counter instead.
/// </summary>
public static class MonotonicClock
{
    private static readonly double TicksToMs = 1000.0 / Stopwatch.Frequency;

    public static long NowMs() => (long)(Stopwatch.GetTimestamp() * TicksToMs);
}
