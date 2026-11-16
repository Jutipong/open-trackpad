namespace RawInputProbe.Ptp;

// Fixed parser for the static PTP report descriptor used by
// MagicTrackpad2ForWindows v2.3 (AAPL_MAGIC_TRACKPAD2_PTP_TLC).
//
// Report ID 0x05, five finger collections of 21 bytes each, then:
//   scan time (4), contact count (1), mechanical force (2), button (1).
public static class Mt2PtpParser
{
    public const byte ReportId = 0x05;
    public const int MaxFingers = 5;
    public const int FingerSize = 21;
    public const int ReportLength = 112;

    private const int ScanTimeOffset = 1 + FingerSize * MaxFingers; // 106
    private const int ContactCountOffset = ScanTimeOffset + 2;      // 108
    private const int ForceOffset = ContactCountOffset + 1;         // 109
    private const int ButtonOffset = ForceOffset + 2;               // 111

    public static PtpFrame Parse(byte[] data)
    {
        if (data.Length == 0)
            throw new InvalidOperationException("Empty input report.");

        var frame = new PtpFrame { ReportId = data[0], Raw = data };
        if (data[0] != ReportId)
            throw new InvalidOperationException($"Unexpected report ID 0x{data[0]:X2} (expected 0x05).");

        if (data.Length > ScanTimeOffset)
            frame.ScanTime = ReadUInt16(data, ScanTimeOffset);
        if (data.Length > ContactCountOffset)
            frame.ContactCount = Math.Min((int)data[ContactCountOffset], MaxFingers);
        if (data.Length > ForceOffset)
            frame.Force = ReadUInt16(data, ForceOffset);
        if (data.Length > ButtonOffset)
            frame.Button = data[ButtonOffset] & 1;

        for (int i = 0; i < MaxFingers; i++)
        {
            int offset = 1 + i * FingerSize;
            if (offset + FingerSize > data.Length) break;

            byte flags = data[offset];
            var contact = new PtpContact
            {
                Index = i,
                Confidence = flags & 1,
                Tip = (flags >> 1) & 1,
                Id = ReadInt32(data, offset + 1),
                X = ReadUInt16(data, offset + 5),
                Y = ReadUInt16(data, offset + 7),
                Width = ReadUInt16(data, offset + 9),
                Height = ReadUInt16(data, offset + 11),
                Pressure = ReadUInt16(data, offset + 13),
                Size = data[offset + 15],
                TouchMajor = data[offset + 16],
                TouchMinor = data[offset + 17],
                Orientation = data[offset + 18],
                FingerType = data[offset + 19],
                State = data[offset + 20],
            };

            if (contact.Tip != 0 || contact.Confidence != 0 || contact.Id != 0)
                frame.Contacts.Add(contact);
        }

        return frame;
    }

    private static ushort ReadUInt16(byte[] data, int offset) =>
        (ushort)(data[offset] | (data[offset + 1] << 8));

    private static int ReadInt32(byte[] data, int offset) =>
        data[offset] | (data[offset + 1] << 8) | (data[offset + 2] << 16) | (data[offset + 3] << 24);
}
