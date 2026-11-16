using System.Text.Json;
using OpenTrackpad.Core.Input;
using OpenTrackpad.Interop.Ptp;

namespace OpenTrackpad.Interop.Tests.Ptp;

public sealed class Mt2ThreeFingerFixtureReplayTests
{
    private const string FixtureName = "mt2-usb-pressure-3finger-2026-11-16.jsonl";

    [Fact]
    public void RealThreeFingerCapture_StartsOnceEndsOnceAndDoesNotCreep()
    {
        var frames = LoadFrames();
        var fsm = new ThreeFingerDragFsm(new ThreeFingerDragOptions
        {
            RequiredContacts = 3,
            StableMs = 40,
            StartThreshold = 25,
            Gain = 0.5,
            StaleFrameMs = 500,
            LiftGraceMs = 60,
            MinSpeedPxPerMs = 0.020,
            ResumeSpeedPxPerMs = 0.040,
        });

        var results = new List<(int Index, int Active, IReadOnlyList<DragAction> Actions)>();
        for (int i = 0; i < frames.Count; i++)
        {
            var frame = frames[i];
            var actions = fsm.Process(frame);
            results.Add((i, frame.Contacts.Count(c => c.IsActive), actions));
        }

        var allActions = results
            .SelectMany(r => r.Actions.Select(a => (r.Index, Action: a)))
            .ToList();

        Assert.Equal(1, allActions.Count(x => x.Action.Kind == DragActionKind.Start));
        Assert.Equal(1, allActions.Count(x => x.Action.Kind == DragActionKind.End));

        (int Start, int End) run = LongestThreeContactRun(results);
        Assert.True(run.End - run.Start > 30, $"unexpected run {run.Start}..{run.End}");

        var start = allActions.Single(x => x.Action.Kind == DragActionKind.Start);
        Assert.InRange(start.Index, run.Start, run.End);

        var end = allActions.Single(x => x.Action.Kind == DragActionKind.End);
        Assert.True(end.Index >= run.End - 1, "drag ended before the fingers lifted");

        // The last 20 frames of the capture are a stationary hold followed by
        // the pressure collapse while lifting. Neither may move the cursor.
        int tailStart = Math.Max(run.Start, run.End - 20);
        var tailMoves = allActions
            .Where(x => x.Index >= tailStart && x.Index <= run.End &&
                        x.Action.Kind == DragActionKind.Move)
            .ToList();
        Assert.Empty(tailMoves);
    }

    private static List<TouchFrame> LoadFrames()
    {
        string path = Path.Combine(
            AppContext.BaseDirectory, "Fixtures", FixtureName);
        Assert.True(File.Exists(path), $"missing fixture: {path}");

        var frames = new List<TouchFrame>();
        DateTimeOffset? origin = null;

        foreach (string line in File.ReadLines(path))
        {
            using var document = JsonDocument.Parse(line);
            JsonElement root = document.RootElement;

            DateTimeOffset time = root.GetProperty("t").GetDateTimeOffset();
            origin ??= time;
            long relativeMs = (long)(time - origin.Value).TotalMilliseconds;

            byte[] raw = Convert.FromHexString(root.GetProperty("hex").GetString()!);
            PtpInputReport report = Mt2PtpReportParser.Parse(raw);
            frames.Add(report.Touch with { TimestampMs = relativeMs });
        }

        return frames;
    }

    private static (int Start, int End) LongestThreeContactRun(
        List<(int Index, int Active, IReadOnlyList<DragAction> Actions)> results)
    {
        int bestStart = 0;
        int bestEnd = 0;
        int? runStart = null;

        for (int i = 0; i < results.Count; i++)
        {
            if (results[i].Active == 3 && runStart is null)
                runStart = i;

            bool isRunEnd = results[i].Active != 3;
            if (runStart is not null && isRunEnd)
            {
                if (i - runStart.Value > bestEnd - bestStart)
                {
                    bestStart = runStart.Value;
                    bestEnd = i;
                }

                runStart = null;
            }
        }

        if (runStart is not null && results.Count - runStart.Value > bestEnd - bestStart)
        {
            bestStart = runStart.Value;
            bestEnd = results.Count;
        }

        return (bestStart, bestEnd);
    }
}
