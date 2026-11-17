using OpenTrackpad.Interop.Driver;

namespace OpenTrackpad.Interop.Tests.Driver;

public sealed class BtIoctlTests
{
    [Fact]
    public void ReloadSettingsMatchesDriverCtlCode()
    {
        Assert.Equal(0x00222000u, BtIoctl.ReloadSettings);
    }

    [Fact]
    public void GetBatteryMatchesDriverCtlCode()
    {
        Assert.Equal(0x00222004u, BtIoctl.GetBattery);
    }

    [Fact]
    public void ActuateMatchesDriverCtlCode()
    {
        Assert.Equal(0x0022A008u, BtIoctl.Actuate);
    }
}
