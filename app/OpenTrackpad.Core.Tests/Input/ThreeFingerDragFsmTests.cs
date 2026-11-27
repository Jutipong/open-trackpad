using OpenTrackpad.Core.Input;

namespace OpenTrackpad.Core.Tests.Input;

public sealed class ThreeFingerDragFsmTests
{
    private static ThreeFingerDragFsm Create(
        double minSpeedPxPerMs = 0.020,
        double resumeSpeedPxPerMs = 0.040,
        int liftGraceMs = 60) => new(new ThreeFingerDragOptions
        {
            RequiredContacts = 3,
            StableMs = 40,
            StartThreshold = 25,
            Gain = 0.5,
            StaleFrameMs = 500,
            LiftGraceMs = liftGraceMs,
            MinSpeedPxPerMs = minSpeedPxPerMs,
            ResumeSpeedPxPerMs = resumeSpeedPxPerMs,
        });

    private static TouchContact Contact(
        int id,
        int x,
        int y,
        bool tip = true,
        bool confidence = true,
        int pressure = 10,
        int fingerType = 0,
        int state = 0) =>
        new(id, tip, confidence, x, y, pressure, fingerType, state);

    private static TouchFrame Frame(long timestampMs, params TouchContact[] contacts) =>
        new(timestampMs, contacts);

    private static TouchContact[] ThreeAt(int offsetX = 0, int offsetY = 0, int pressure = 10) => new[]
    {
        Contact(1, 100 + offsetX, 100 + offsetY, pressure: pressure),
        Contact(2, 200 + offsetX, 100 + offsetY, pressure: pressure),
        Contact(3, 300 + offsetX, 100 + offsetY, pressure: pressure),
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
    public void Type7PalmWithTip_DoesNotCountAsFinger()
    {
        var fsm = Create();
        var palm = Contact(9, 400, 200, fingerType: 7, state: 4);

        Assert.Empty(fsm.Process(Frame(0, ThreeAt().Take(2).Append(palm).ToArray())));
        var actions = fsm.Process(Frame(100,
            ThreeAt(30).Take(2).Append(palm with { X = 430 }).ToArray()));

        Assert.Empty(actions);
        Assert.False(fsm.IsDragging);
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
    public void FingerLift_EndsDragAfterGrace()
    {
        var fsm = StartDrag();

        var actions = fsm.Process(Frame(100, ThreeAt(30).Take(2).ToArray()));

        Assert.Empty(actions);
        Assert.True(fsm.IsDragging);

        actions = fsm.Process(Frame(170, ThreeAt(30).Take(2).ToArray()));

        Assert.Single(actions);
        Assert.Equal(DragActionKind.End, actions[0].Kind);
        Assert.False(fsm.IsDragging);
    }

    [Fact]
    public void ContactSetChange_EndsDragAfterGrace()
    {
        var fsm = StartDrag();
        var differentIds = new[]
        {
            Contact(1, 130, 100),
            Contact(2, 230, 100),
            Contact(4, 330, 100),
        };

        Assert.Empty(fsm.Process(Frame(100, differentIds)));
        var actions = fsm.Process(Frame(170, differentIds));

        Assert.Single(actions);
        Assert.Equal(DragActionKind.End, actions[0].Kind);
        Assert.False(fsm.IsDragging);
    }

    [Fact]
    public void ExtraActiveContact_DoesNotEndDrag()
    {
        var fsm = StartDrag();
        var fourthFinger = Contact(9, 500, 500);

        var actions = fsm.Process(Frame(100, ThreeAt(40).Append(fourthFinger).ToArray()));

        Assert.DoesNotContain(actions, a => a.Kind == DragActionKind.End);
        Assert.Contains(actions, a => a.Kind == DragActionKind.Move);
        Assert.True(fsm.IsDragging);
    }

    [Fact]
    public void ContactDropoutWithinGrace_KeepsDragWithoutJump()
    {
        var fsm = StartDrag();

        Assert.Empty(fsm.Process(Frame(100, ThreeAt(30).Take(2).ToArray())));
        Assert.True(fsm.IsDragging);

        // The contact returns: re-anchor without moving the cursor.
        Assert.Empty(fsm.Process(Frame(150, ThreeAt(30))));
        Assert.True(fsm.IsDragging);

        var actions = fsm.Process(Frame(161, ThreeAt(40)));
        Assert.Contains(actions, a => a.Kind == DragActionKind.Move);
        Assert.True(fsm.IsDragging);
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
        var fsm = StartDrag(Create(minSpeedPxPerMs: 0, resumeSpeedPxPerMs: 0));

        Assert.Empty(fsm.Process(Frame(100, ThreeAt(31)))); // 0.5 px -> no move yet
        var actions = fsm.Process(Frame(110, ThreeAt(32))); // 0.5 + residual 0.5 -> 1 px

        Assert.Single(actions);
        Assert.Equal(1, actions[0].DeltaX);
    }

    [Fact]
    public void StationaryDrift_DoesNotMove()
    {
        var fsm = StartDrag();
        var extra = new int[3];

        for (int frame = 0; frame < 30; frame++)
        {
            extra[frame % 3]++;
            var contacts = new[]
            {
                Contact(1, 100 + 30 + extra[0], 100),
                Contact(2, 200 + 30 + extra[1], 100),
                Contact(3, 300 + 30 + extra[2], 100),
            };

            var actions = fsm.Process(Frame(61 + frame * 11, contacts));
            Assert.DoesNotContain(actions, a => a.Kind == DragActionKind.Move);
        }

        Assert.True(fsm.IsDragging);
    }

    [Fact]
    public void FastMoveAfterStillness_ResumesMovement()
    {
        var fsm = StartDrag();

        for (int frame = 0; frame < 10; frame++)
            fsm.Process(Frame(61 + frame * 11, ThreeAt(30)));

        var actions = fsm.Process(Frame(180, ThreeAt(60)));

        Assert.Contains(actions, a => a.Kind == DragActionKind.Move);
    }

    [Fact]
    public void PressureCollapse_FreezesMovement()
    {
        var fsm = StartDrag();

        var actions = fsm.Process(Frame(100, ThreeAt(60, pressure: 1)));

        Assert.Empty(actions);
        Assert.True(fsm.IsDragging);
    }

    [Fact]
    public void ScanTimeBurst_UsesDeviceDelta()
    {
        var fsm = Create();
        fsm.Process(Frame(0, ThreeAt()) with { ScanTime = 1000 });
        var start = fsm.Process(Frame(50, ThreeAt(30)) with { ScanTime = 1100 });
        Assert.Single(start);

        // Two samples delivered 1 ms apart in wall time but 11 ms apart on the
        // device. A 1/3 raw-unit drift is tremor at 11 ms, but would look fast
        // at 1 ms if the wall clock were used.
        fsm.Process(Frame(51, ThreeAt(30)) with { ScanTime = 1210 });
        var drifted = new[]
        {
            Contact(1, 100 + 30, 100),
            Contact(2, 200 + 30, 100),
            Contact(3, 300 + 30 + 1, 100),
        };

        var actions = fsm.Process(Frame(52, drifted) with { ScanTime = 1320 });

        Assert.Empty(actions);
    }

    [Fact]
    public void DuplicateScanTime_IsIgnored()
    {
        var fsm = Create();
        fsm.Process(Frame(0, ThreeAt()) with { ScanTime = 1000 });
        Assert.Single(fsm.Process(Frame(50, ThreeAt(30)) with { ScanTime = 1100 }));

        Assert.Empty(fsm.Process(Frame(60, ThreeAt(60)) with { ScanTime = 1100 }));

        var actions = fsm.Process(Frame(61, ThreeAt(60)) with { ScanTime = 1210 });
        Assert.Contains(actions, a => a.Kind == DragActionKind.Move);
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

    [Fact]
    public void Hold_KeepsStableContacts_SoDragStartsRightAfter()
    {
        var fsm = Create();

        Assert.Empty(fsm.Process(Frame(0, ThreeAt())));
        fsm.Hold(Frame(20, ThreeAt(20)));
        fsm.Hold(Frame(40, ThreeAt(40)));
        Assert.False(fsm.IsDragging);

        var actions = fsm.Process(Frame(50, ThreeAt(50)));

        Assert.Single(actions);
        Assert.Equal(DragActionKind.Start, actions[0].Kind);
    }

    [Fact]
    public void Hold_NeverStartsDrag()
    {
        var fsm = Create();

        fsm.Hold(Frame(0, ThreeAt()));
        fsm.Hold(Frame(100, ThreeAt(200)));

        Assert.False(fsm.IsDragging);
    }

    private static ThreeFingerDragFsm StartDrag(ThreeFingerDragFsm? fsm = null)
    {
        fsm ??= Create();
        fsm.Process(Frame(0, ThreeAt()));
        var actions = fsm.Process(Frame(50, ThreeAt(30)));
        Assert.Single(actions);
        Assert.Equal(DragActionKind.Start, actions[0].Kind);
        return fsm;
    }
}
