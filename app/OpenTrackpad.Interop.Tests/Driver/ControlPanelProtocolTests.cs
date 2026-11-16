using OpenTrackpad.Interop.Driver;

namespace OpenTrackpad.Interop.Tests.Driver;

public sealed class ControlPanelProtocolTests
{
    [Fact]
    public void Requests_HaveExpectedBytes()
    {
        Assert.Equal(new byte[] { 0x00 }, ControlPanelProtocol.ReloadRequest());
        Assert.Equal(new byte[] { 0x01 }, ControlPanelProtocol.BatteryRequest());
    }

    [Theory]
    [InlineData(0x040415u, new byte[] { 0x02, 0x15, 0x04, 0x04 })]
    [InlineData(0x060617u, new byte[] { 0x02, 0x17, 0x06, 0x06 })]
    [InlineData(0x08081Eu, new byte[] { 0x02, 0x1E, 0x08, 0x08 })]
    public void ActuateRequest_EncodesLittleEndianStrength(uint strength, byte[] expected)
    {
        Assert.Equal(expected, ControlPanelProtocol.ActuateRequest(strength));
    }

    [Fact]
    public void ActuateRequest_RejectsOver24Bits()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ControlPanelProtocol.ActuateRequest(0x1000000));
    }

    [Theory]
    [InlineData(0x00, 87, true)]
    [InlineData(0xFF, 0, false)]
    [InlineData(0xFE, 0, false)]
    public void ParseResponse_ReturnsStatusAndValue(byte status, byte value, bool ok)
    {
        var response = ControlPanelProtocol.ParseResponse(new[] { status, value });

        Assert.Equal(status, response.Status);
        Assert.Equal(value, response.Value);
        Assert.Equal(ok, response.Ok);
    }

    [Fact]
    public void ParseResponse_RejectsShortResponse()
    {
        Assert.Throws<InvalidDataException>(() =>
            ControlPanelProtocol.ParseResponse(new byte[] { 0x00 }));
    }
}
