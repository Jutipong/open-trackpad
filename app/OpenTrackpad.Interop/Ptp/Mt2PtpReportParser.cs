using OpenTrackpad.Core.Input;

namespace OpenTrackpad.Interop.Ptp;

public sealed record PtpInputReport(
    TouchFrame Touch,
    int ContactCount,
    int ScanTime,
    int Force,
    int Button);

/// <summary>
/// Parser for the static Magic Trackpad 2 PTP report emitted by
/// MagicTrackpad2ForWindows v2.3 (report ID 0x05, 112 bytes).
/// </summary>
public static class Mt2PtpReportParser
{
    public const byte ReportId = 0x05;
    public const int MaxContacts = 5;
    public const int ContactSize = 21;
    public const int ReportLength = 112;

    private const int ScanTimeOffset = 1 + ContactSize * MaxContacts; // 106
    private const int ContactCountOffset = ScanTimeOffset + 2;        // 108
    private const int ForceOffset = ContactCountOffset + 1;           // 109
    private const int ButtonOffset = ForceOffset + 2;                 // 111

    public static PtpInputReport Parse(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Length < ReportLength)
            throw new ArgumentException(
                $"MT2 PTP report must be {ReportLength} bytes, got {data.Length}.", nameof(data));
        if (data[0] != ReportId)
            throw new ArgumentException(
                $"Unexpected report ID 0x{data[0]:X2} (expected 0x05).", nameof(data));

        var contacts = new List<TouchContact>(MaxContacts);
        for (int i = 0; i < MaxContacts; i++)
        {
            int offset = 1 + i * ContactSize;
            byte flags = data[offset];
            contacts.Add(new TouchContact(
                Id: ReadInt32(data, offset + 1),
                Tip: (flags & 0x02) != 0,
                Confidence: (flags & 0x01) != 0,
                X: ReadUInt16(data, offset + 5),
                Y: ReadUInt16(data, offset + 7),
                Pressure: ReadUInt16(data, offset + 13),
                FingerType: data[offset + 19],
                State: data[offset + 20]));
        }

        int scanTime = ReadUInt16(data, ScanTimeOffset);
        var touch = new TouchFrame(MonotonicClock.NowMs(), contacts)
        {
            ScanTime = scanTime,
        };
        return new PtpInputReport(
            touch,
            data[ContactCountOffset],
            scanTime,
            ReadUInt16(data, ForceOffset),
            data[ButtonOffset] & 1);
    }

    private static ushort ReadUInt16(byte[] data, int offset) =>
        (ushort)(data[offset] | (data[offset + 1] << 8));

    private static int ReadInt32(byte[] data, int offset) =>
        data[offset] | (data[offset + 1] << 8) | (data[offset + 2] << 16) | (data[offset + 3] << 24);
}
