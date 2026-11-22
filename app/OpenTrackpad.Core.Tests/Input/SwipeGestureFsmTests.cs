using OpenTrackpad.Core.Input;

namespace OpenTrackpad.Core.Tests.Input;

public sealed class SwipeGestureFsmTests
{
    private static SwipeGestureFsm Create(
        int contacts = 3,
        bool horizontalOnly = false,
        double minSpeedPxPerMs = 0.25) => new(new SwipeGestureOptions
        {
            RequiredContacts = contacts,
            StableMs = 40,
            MaxGestureMs = 600,
            StartThreshold = 60,
            CandidateThreshold = 12,
            AxisRatio = 1.5,
            MinSpeedPxPerMs = minSpeedPxPerMs,
            HorizontalOnly = horizontalOnly,
        });

    private static TouchContact Contact(int id, int x, int y) =>
        new(id, Tip: true, Confidence: true, X: x, Y: y, Pressure: 10);

    private static TouchContact[] Row(int contacts, int offsetX = 0, int offsetY = 0) =>
        Enumerable.Range(1, contacts)
            .Select(i => Contact(i, i * 100 + offsetX, 100 + offsetY))
            .ToArray();

    private static TouchFrame Frame(long timestampMs, params TouchContact[] contacts) =>
        new(timestampMs, contacts);

    private static SwipeGesture? SwipeFast(
        SwipeGestureFsm fsm,
        int contacts,
        int dx,
        int dy,
        out bool candidateSeen)
    {
        candidateSeen = false;
        fsm.Process(Frame(0, Row(contacts)));
        int frames = 6;
        for (int i = 1; i <= frames; i++)
        {
            double fraction = (double)i / frames;
            int x = (int)Math.Round(dx * fraction);
            int y = (int)Math.Round(dy * fraction);
            SwipeGesture? gesture = fsm.Process(
                Frame(i * 10, Row(contacts, x, y)));

            if (fsm.IsCandidate)
                candidateSeen = true;

            if (gesture is not null)
                return gesture;
        }

        return null;
    }

    [Fact]
    public void FourFingerFastSwipeRight_EmitsRight()
    {
        var fsm = Create(contacts: 4);

        SwipeGesture? gesture = SwipeFast(fsm, 4, 80, 0, out _);

        Assert.NotNull(gesture);
        Assert.Equal(SwipeDirection.Right, gesture.Value.Direction);
        Assert.Equal(4, gesture.Value.ContactCount);
    }

    [Fact]
    public void FourFingerFastSwipeUp_EmitsUp()
    {
        var fsm = Create(contacts: 4);

        SwipeGesture? gesture = SwipeFast(fsm, 4, 0, -80, out _);

        Assert.NotNull(gesture);
        Assert.Equal(SwipeDirection.Up, gesture.Value.Direction);
    }

    [Fact]
    public void ThreeFingerFastHorizontalSwipe_IsCandidateBeforeItFires()
    {
        var fsm = Create(contacts: 3, horizontalOnly: true);

        SwipeGesture? gesture = SwipeFast(fsm, 3, -80, 0, out bool candidateSeen);

        Assert.True(candidateSeen);
        Assert.NotNull(gesture);
        Assert.Equal(SwipeDirection.Left, gesture.Value.Direction);
    }

    [Fact]
    public void ThreeFingerVerticalSwipe_DoesNothing()
    {
        var fsm = Create(contacts: 3, horizontalOnly: true);

        SwipeGesture? gesture = SwipeFast(fsm, 3, 0, -100, out bool candidateSeen);

        Assert.Null(gesture);
        Assert.False(candidateSeen);
    }

    [Fact]
    public void ThreeFingerSlowHorizontalDrag_DoesNothing()
    {
        var fsm = Create(contacts: 3, horizontalOnly: true);

        fsm.Process(Frame(0, Row(3)));
        SwipeGesture? gesture = null;
        for (int i = 1; i <= 20; i++)
        {
            gesture = fsm.Process(Frame(i * 20, Row(3, i * 3)));
            if (gesture is not null)
                break;
        }

        Assert.Null(gesture);
    }

    [Fact]
    public void WrongNumberOfContacts_DoesNothing()
    {
        var fsm = Create(contacts: 4);

        SwipeGesture? gesture = SwipeFast(fsm, 3, 100, 0, out _);

        Assert.Null(gesture);
    }

    [Fact]
    public void GestureFiresOnceUntilContactsLift()
    {
        var fsm = Create(contacts: 4);

        SwipeGesture? first = SwipeFast(fsm, 4, 80, 0, out _);
        Assert.NotNull(first);

        SwipeGesture? second = fsm.Process(Frame(100, Row(4, 200, 0)));
        Assert.Null(second);

        fsm.Process(Frame(110, Row(2)));
        SwipeGesture? afterLift = SwipeFast(fsm, 4, 80, 0, out _);
        Assert.NotNull(afterLift);
    }

    [Fact]
    public void DiagonalMovement_IsIgnored()
    {
        var fsm = Create(contacts: 4);

        SwipeGesture? gesture = SwipeFast(fsm, 4, 80, 80, out _);

        Assert.Null(gesture);
    }

    [Fact]
    public void PalmContact_IsIgnored()
    {
        var fsm = Create(contacts: 4);
        var palm = new TouchContact(9, Tip: true, Confidence: true,
            X: 500, Y: 200, Pressure: 30, FingerType: 7, State: 4);

        fsm.Process(Frame(0, Row(4).Append(palm).ToArray()));
        SwipeGesture? gesture = null;
        for (int i = 1; i <= 6; i++)
        {
            gesture = fsm.Process(Frame(i * 10,
                Row(4, i * 15).Append(palm with { X = 500 + i * 15 }).ToArray()));
            if (gesture is not null)
                break;
        }

        Assert.NotNull(gesture);
        Assert.Equal(SwipeDirection.Right, gesture.Value.Direction);
    }

    [Fact]
    public void RealisticScale_FastThreeFingerSwipe_Fires()
    {
        var fsm = new SwipeGestureFsm(new SwipeGestureOptions
        {
            RequiredContacts = 3,
            HorizontalOnly = true,
            StableMs = 40,
            StartThreshold = 500,
            CandidateThreshold = 20,
            MinSpeedPxPerMs = 2.0,
        });

        fsm.Process(Frame(0, Row(3)));
        SwipeGesture? gesture = null;
        for (int i = 1; i <= 8; i++)
        {
            gesture = fsm.Process(Frame(i * 10, Row(3, i * 100)));
            if (gesture is not null)
                break;
        }

        Assert.NotNull(gesture);
        Assert.Equal(SwipeDirection.Right, gesture.Value.Direction);
    }

    [Fact]
    public void RealisticScale_SlowThreeFingerDrag_DoesNotFire()
    {
        var fsm = new SwipeGestureFsm(new SwipeGestureOptions
        {
            RequiredContacts = 3,
            HorizontalOnly = true,
            StableMs = 40,
            StartThreshold = 500,
            CandidateThreshold = 20,
            MinSpeedPxPerMs = 2.0,
        });

        fsm.Process(Frame(0, Row(3)));
        SwipeGesture? gesture = null;
        for (int i = 1; i <= 60; i++)
        {
            gesture = fsm.Process(Frame(i * 10, Row(3, i * 10)));
            Assert.False(fsm.IsCandidate);
            if (gesture is not null)
                break;
        }

        Assert.Null(gesture);
    }
}
