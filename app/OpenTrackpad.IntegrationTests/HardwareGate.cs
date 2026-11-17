namespace OpenTrackpad.IntegrationTests;

/// <summary>
/// Hardware tests only run when OPENTRACKPAD_HW=1. They are skipped (reported
/// as passing) on machines without a Magic Trackpad 2.
/// </summary>
internal static class HardwareGate
{
    public static bool Enabled =>
        string.Equals(
            Environment.GetEnvironmentVariable("OPENTRACKPAD_HW"),
            "1",
            StringComparison.Ordinal);
}
