using OpenTrackpad.Interop.Ptp;

namespace OpenTrackpad.Interop.Tests.Ptp;

public sealed class Mt2PtpReportParserTests
{
    private const int ReportLength = Mt2PtpReportParser.ReportLength;

    [Fact]
    public void Parse_ReadsContactFieldsAndHeader()
    {
        byte[] report = BuildReport();
        SetContact(report, 0, id: 7, tip: true, confidence: true, x: 1234, y: 5678, pressure: 42);
        WriteUInt16(report, 106, 4321);
        report[108] = 1;
        WriteUInt16(report, 109, 42);
        report[111] = 1;

        var parsed = Mt2PtpReportParser.Parse(report);

        Assert.Equal(7, parsed.Touch.Contacts[0].Id);
        Assert.True(parsed.Touch.Contacts[0].Tip);
        Assert.True(parsed.Touch.Contacts[0].Confidence);
        Assert.Equal(1234, parsed.Touch.Contacts[0].X);
        Assert.Equal(5678, parsed.Touch.Contacts[0].Y);
        Assert.Equal(42, parsed.Touch.Contacts[0].Pressure);
        Assert.Equal(4321, parsed.ScanTime);
        Assert.Equal(4321, parsed.Touch.ScanTime);
        Assert.Equal(1, parsed.ContactCount);
        Assert.Equal(42, parsed.Force);
        Assert.Equal(1, parsed.Button);
    }

    [Fact]
    public void Parse_KeepsPalmContactButMarksItInactive()
    {
        byte[] report = BuildReport();
        SetContact(report, 0, id: 1, tip: true, confidence: true, x: 100, y: 100, pressure: 20);
        SetContact(report, 1, id: 2, tip: true, confidence: true, x: 200, y: 100, pressure: 20);
        SetContact(report, 2, id: 3, tip: true, confidence: true, x: 300, y: 100, pressure: 20);
        SetContact(report, 3, id: 9, tip: false, confidence: true, x: 400, y: 200, pressure: 0);

        var parsed = Mt2PtpReportParser.Parse(report);

        Assert.Equal(4, parsed.Touch.Contacts.Count(c => c.Confidence));
        Assert.Equal(3, parsed.Touch.Contacts.Count(c => c.IsActive));
        Assert.False(parsed.Touch.Contacts[3].IsActive);
    }

    [Fact]
    public void Parse_Type7PalmWithTipIsInactive()
    {
        byte[] report = BuildReport();
        SetContact(report, 0, id: 1, tip: true, confidence: true, x: 100, y: 100, pressure: 10);
        SetContact(report, 1, id: 2, tip: true, confidence: true, x: 200, y: 100, pressure: 10);
        SetContact(report, 2, id: 3, tip: true, confidence: true, x: 300, y: 100, pressure: 10);
        SetContact(report, 3, id: 4, tip: true, confidence: true, x: 400, y: 200, pressure: 0,
            fingerType: 7, state: 4);

        var parsed = Mt2PtpReportParser.Parse(report);

        Assert.True(parsed.Touch.Contacts[3].Tip);
        Assert.True(parsed.Touch.Contacts[3].Confidence);
        Assert.False(parsed.Touch.Contacts[3].IsActive);
        Assert.Equal(3, parsed.Touch.Contacts.Count(c => c.IsActive));
    }

    [Fact]
    public void Parse_RejectsWrongReportId()
    {
        byte[] report = BuildReport();
        report[0] = 0x06;

        Assert.Throws<ArgumentException>(() => Mt2PtpReportParser.Parse(report));
    }

    [Fact]
    public void Parse_RejectsShortReport()
    {
        Assert.Throws<ArgumentException>(() => Mt2PtpReportParser.Parse(new byte[100]));
    }

    private static byte[] BuildReport()
    {
        var report = new byte[ReportLength];
        report[0] = Mt2PtpReportParser.ReportId;
        return report;
    }

    private static void SetContact(
        byte[] report, int index, int id, bool tip, bool confidence, int x, int y, int pressure,
        int fingerType = 4, int state = 4)
    {
        int offset = 1 + index * Mt2PtpReportParser.ContactSize;
        report[offset] = (byte)((confidence ? 1 : 0) | (tip ? 2 : 0));
        WriteInt32(report, offset + 1, id);
        WriteUInt16(report, offset + 5, (ushort)x);
        WriteUInt16(report, offset + 7, (ushort)y);
        WriteUInt16(report, offset + 9, 300);
        WriteUInt16(report, offset + 11, 200);
        WriteUInt16(report, offset + 13, (ushort)pressure);
        report[offset + 19] = (byte)fingerType;
        report[offset + 20] = (byte)state;
    }

    private static void WriteUInt16(byte[] data, int offset, ushort value)
    {
        data[offset] = (byte)(value & 0xFF);
        data[offset + 1] = (byte)(value >> 8);
    }

    private static void WriteInt32(byte[] data, int offset, int value)
    {
        data[offset] = (byte)(value & 0xFF);
        data[offset + 1] = (byte)((value >> 8) & 0xFF);
        data[offset + 2] = (byte)((value >> 16) & 0xFF);
        data[offset + 3] = (byte)((value >> 24) & 0xFF);
    }
}
