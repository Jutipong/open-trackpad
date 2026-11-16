using RawInputProbe.Hid;

namespace RawInputProbe.Ptp;

public sealed class PtpContact
{
    public int Index { get; init; }
    public int Id { get; set; }
    public int Tip { get; set; }
    public int Confidence { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public int Pressure { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public int Size { get; set; }
    public int TouchMajor { get; set; }
    public int TouchMinor { get; set; }
    public int Orientation { get; set; }
    public int FingerType { get; set; }
    public int State { get; set; }

    public override string ToString() =>
        $"id={Id} tip={Tip} conf={Confidence} x={X} y={Y} p={Pressure} w={Width} h={Height}";
}

public sealed class PtpFrame
{
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.Now;
    public byte ReportId { get; init; }
    public required byte[] Raw { get; init; }
    public int ContactCount { get; set; } = -1;
    public int ScanTime { get; set; } = -1;
    public int Force { get; set; } = -1;
    public int Button { get; set; } = -1;
    public List<PtpContact> Contacts { get; } = new();

    public override string ToString()
    {
        string contacts = Contacts.Count == 0
            ? "(none)"
            : string.Join(" | ", Contacts.Select(c => c.ToString()));
        return $"t={Timestamp:HH:mm:ss.fff} rid=0x{ReportId:X2} len={Raw.Length} " +
               $"count={ContactCount} scan={ScanTime} force={Force} button={Button} :: {contacts}";
    }
}

public static class PtpParser
{
    public static PtpFrame Parse(HidDescriptor descriptor, byte[] data)
    {
        byte reportId = descriptor.UsesReportIds ? data[0] : (byte)0;
        int payloadOffset = descriptor.UsesReportIds ? 1 : 0;
        var report = descriptor.GetReport(HidReportKind.Input, reportId)
            ?? throw new InvalidOperationException(
                $"No input report 0x{reportId:X2} in descriptor (report ids={descriptor.UsesReportIds}).");

        var frame = new PtpFrame
        {
            ReportId = reportId,
            Raw = data,
        };

        PtpContact? current = null;
        foreach (var field in report.Fields)
        {
            if (field.IsConstant) continue;

            // PTP finger fields are declared with count 1. If a descriptor uses a
            // larger count, only the first element is interpreted here.
            uint usage = field.UsageAt(0);
            uint page = usage >> 16;
            uint id = usage & 0xFFFF;
            int value = ReadLogical(data.AsSpan(payloadOffset), field, 0);

            if (page == 0x0D)
            {
                switch (id)
                {
                    case 0x51: // Contact Identifier
                        current = new PtpContact { Index = frame.Contacts.Count, Id = value };
                        frame.Contacts.Add(current);
                        break;
                    case 0x42: // Tip Switch
                        current ??= EnsureContact(frame);
                        current.Tip = value;
                        break;
                    case 0x47: // Confidence
                        current ??= EnsureContact(frame);
                        current.Confidence = value;
                        break;
                    case 0x30: // Tip Pressure
                        current ??= EnsureContact(frame);
                        current.Pressure = value;
                        break;
                    case 0x48: // Width
                        current ??= EnsureContact(frame);
                        current.Width = value;
                        break;
                    case 0x49: // Height
                        current ??= EnsureContact(frame);
                        current.Height = value;
                        break;
                    case 0x54: // Contact Count
                        frame.ContactCount = value;
                        break;
                    case 0x56: // Scan Time
                        frame.ScanTime = value;
                        break;
                }
            }
            else if (page == 0x01)
            {
                switch (id)
                {
                    case 0x30: // X
                        current ??= EnsureContact(frame);
                        current.X = value;
                        break;
                    case 0x31: // Y
                        current ??= EnsureContact(frame);
                        current.Y = value;
                        break;
                }
            }
        }

        if (frame.ContactCount < 0) frame.ContactCount = frame.Contacts.Count;
        return frame;
    }

    private static PtpContact EnsureContact(PtpFrame frame)
    {
        if (frame.Contacts.Count == 0)
            frame.Contacts.Add(new PtpContact { Index = 0 });
        return frame.Contacts[^1];
    }

    public static int ReadLogical(ReadOnlySpan<byte> payload, HidField field, int element)
    {
        int bitOffset = field.BitOffset + element * field.BitSize;
        uint raw = 0;
        for (int i = 0; i < field.BitSize && i < 32; i++)
        {
            int bit = bitOffset + i;
            int byteIndex = bit >> 3;
            if (byteIndex >= payload.Length) break;
            if ((payload[byteIndex] & (1 << (bit & 7))) != 0)
                raw |= 1u << i;
        }

        if (field.LogicalMinimum < 0 && field.BitSize > 0 && field.BitSize < 32)
        {
            int signBit = 1 << (field.BitSize - 1);
            if ((raw & signBit) != 0)
                return (int)raw - (1 << field.BitSize);
        }

        return (int)raw;
    }
}
