namespace OpenTrackpad.Core.Input;

public readonly record struct TouchContact(
    int Id,
    bool Tip,
    bool Confidence,
    int X,
    int Y,
    int Pressure)
{
    public bool IsActive => Tip && Confidence;
}

public sealed record TouchFrame(long TimestampMs, IReadOnlyList<TouchContact> Contacts);

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
