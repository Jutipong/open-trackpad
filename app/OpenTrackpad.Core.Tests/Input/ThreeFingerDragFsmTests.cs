using OpenTrackpad.Core.Input;

namespace OpenTrackpad.Core.Tests.Input;

public sealed class ThreeFingerDragFsmTests
{
    private static ThreeFingerDragFsm Create() => new(new ThreeFingerDragOptions
    {
        RequiredContacts = 3,
        StableMs = 40,
        StartThreshold = 25,
        Gain = 0.5,
        StaleFrameMs = 500,
    });

    private static TouchContact Contact(int id, int x, int y, bool tip = true, bool confidence = true) =>
        new(id, tip, confidence, x, y, 10);

    private static TouchFrame Frame(long timestampMs, params TouchContact[] contacts) =>
        new(timestampMs, contacts);

    private static TouchContact[] ThreeAt(int offsetX = 0, int offsetY = 0) => new[]
    {
        Contact(1, 100 + offsetX, 100 + offsetY),
        Contact(2, 200 + offsetX, 100 + offsetY),
        Contact(3, 300 + offsetX, 100 + offsetY),
    };

    [Fact]
    public void ThreeStableContacts_StartOnlyAfterStableTimeAndMovement()
    {
        var fsm = Create();

        Assert.Empty(fsm.Process(Frame(0, ThreeAt())));
        Assert.Empty(fsm.Process(Frame(20, ThreeAt(10))));
        Assert.Empty(fsm.Process(Frame(39, ThreeAt(20))));

        var actions = fsm.Process(Frame(50, ThreeAt(30)));

        Assert.Single(actions);
        Assert.Equal(DragActionKind.Start, actions[0].Kind);
        Assert.True(fsm.IsDragging);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(5)]
    public void WrongNumberOfActiveContacts_DoesNotStart(int count)
    {
        var fsm = Create();
        var contacts = Enumerable.Range(1, count)
            .Select(i => Contact(i, i * 100, 100))
            .ToArray();

        Assert.Empty(fsm.Process(Frame(0, contacts)));
        var actions = fsm.Process(Frame(100, contacts.Select(c => c with { X = c.X + 100 }).ToArray()));

        Assert.Empty(actions);
        Assert.False(fsm.IsDragging);
    }

    [Fact]
    public void InactiveFourthContact_DoesNotBlockThreeFingerStart()
    {
        var fsm = Create();
        var ghost = Contact(9, 400, 200, tip: false, confidence: true);

        fsm.Process(Frame(0, ThreeAt().Append(ghost).ToArray()));
        var actions = fsm.Process(Frame(100, ThreeAt(30).Append(ghost).ToArray()));

        Assert.Single(actions);
        Assert.Equal(DragActionKind.Start, actions[0].Kind);
    }

    [Fact]
    public void MovementBelowThreshold_DoesNotStart()
    {
        var fsm = Create();

        fsm.Process(Frame(0, ThreeAt()));
        var actions = fsm.Process(Frame(100, ThreeAt(24)));

        Assert.Empty(actions);
        Assert.False(fsm.IsDragging);
    }

    [Fact]
    public void JitterAroundStartPoint_DoesNotStart()
    {
        var fsm = Create();

        fsm.Process(Frame(0, ThreeAt()));
        fsm.Process(Frame(50, ThreeAt(10)));
        Assert.Empty(fsm.Process(Frame(60, ThreeAt(-12))));
        Assert.Empty(fsm.Process(Frame(70, ThreeAt(5))));

        Assert.False(fsm.IsDragging);
    }

    [Fact]
    public void FingerLift_EndsDrag()
    {
        var fsm = StartDrag();

        var actions = fsm.Process(Frame(100, ThreeAt(30).Take(2).ToArray()));

        Assert.Single(actions);
        Assert.Equal(DragActionKind.End, actions[0].Kind);
        Assert.False(fsm.IsDragging);
    }

    [Fact]
    public void ContactSetChange_EndsDrag()
    {
        var fsm = StartDrag();
        var differentIds = new[]
        {
            Contact(1, 130, 100),
            Contact(2, 230, 100),
            Contact(4, 330, 100),
        };

        var actions = fsm.Process(Frame(100, differentIds));

        Assert.Single(actions);
        Assert.Equal(DragActionKind.End, actions[0].Kind);
        Assert.False(fsm.IsDragging);
    }

    [Fact]
    public void Move_ScalesByGain()
    {
        var fsm = StartDrag();

        var actions = fsm.Process(Frame(100, ThreeAt(40)));

        Assert.Single(actions);
        Assert.Equal(DragActionKind.Move, actions[0].Kind);
        Assert.Equal(5, actions[0].DeltaX);
        Assert.Equal(0, actions[0].DeltaY);
    }

    [Fact]
    public void Move_AccumulatesSubPixelResidual()
    {
        var fsm = StartDrag();

        Assert.Empty(fsm.Process(Frame(100, ThreeAt(31)))); // 0.5 px -> no move yet
        var actions = fsm.Process(Frame(110, ThreeAt(32))); // 0.5 + residual 0.5 -> 1 px

        Assert.Single(actions);
        Assert.Equal(1, actions[0].DeltaX);
    }

    [Fact]
    public void Tick_ReleasesAfterStaleFrames()
    {
        var fsm = StartDrag();

        Assert.Empty(fsm.Tick(500));
        var actions = fsm.Tick(601);

        Assert.Single(actions);
        Assert.Equal(DragActionKind.End, actions[0].Kind);
        Assert.False(fsm.IsDragging);
    }

    [Fact]
    public void Reset_ReleasesWhenDragging()
    {
        var fsm = StartDrag();

        var actions = fsm.Reset("mode off");

        Assert.Single(actions);
        Assert.Equal(DragActionKind.End, actions[0].Kind);
        Assert.Equal("mode off", actions[0].Reason);
        Assert.False(fsm.IsDragging);
    }

    [Fact]
    public void Reset_WhenIdle_DoesNotEmitEnd()
    {
        var fsm = Create();

        Assert.Empty(fsm.Reset("idle"));
    }

    [Fact]
    public void InactiveContactsOnly_DoNotStart()
    {
        var fsm = Create();
        var contacts = new[]
        {
            Contact(1, 100, 100, tip: false),
            Contact(2, 200, 100, tip: false),
            Contact(3, 300, 100, tip: false),
        };

        fsm.Process(Frame(0, contacts));
        Assert.Empty(fsm.Process(Frame(100, contacts.Select(c => c with { X = c.X + 100 }).ToArray())));
        Assert.False(fsm.IsDragging);
    }

    private static ThreeFingerDragFsm StartDrag()
    {
        var fsm = Create();
        fsm.Process(Frame(0, ThreeAt()));
        var actions = fsm.Process(Frame(50, ThreeAt(30)));
        Assert.Single(actions);
        Assert.Equal(DragActionKind.Start, actions[0].Kind);
        return fsm;
    }
}
