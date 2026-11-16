namespace OpenTrackpad.Core.Input;

public readonly record struct TouchContact(
    int Id,
    bool Tip,
    bool Confidence,
    int X,
    int Y,
    int Pressure,
    int FingerType = 0,
    int State = 0)
{
    // MT2 firmware reports palms as Finger 6 (on the pad) and 7 (floating at
    // the edge). The v2.3 driver only clears Confidence for finger 6, so a
    // type 7 palm can still arrive with Tip=1 / Confidence=1.
    // States 1/2 are transition/floating and 6/7 are near-field or unknown;
    // only 3..5 are real surface contacts. State 0 means "not reported".
    public bool IsActive =>
        Tip &&
        Confidence &&
        FingerType is not (6 or 7) &&
        (State == 0 || State is >= 3 and <= 5);
}

public sealed record TouchFrame(long TimestampMs, IReadOnlyList<TouchContact> Contacts)
{
    /// <summary>
    /// Device scan time in 0.1 ms units, or 0 when the report has no scan time.
    /// </summary>
    public int ScanTime { get; init; }
}

public enum DragActionKind
{
    Start,
    Move,
    End,
}

public readonly record struct DragAction(
    DragActionKind Kind,
    int DeltaX = 0,
    int DeltaY = 0,
    string? Reason = null);
