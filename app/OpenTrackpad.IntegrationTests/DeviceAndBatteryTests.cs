using OpenTrackpad.Interop.Driver;
using OpenTrackpad.Interop.RawInput;

namespace OpenTrackpad.IntegrationTests;

[Trait("Hardware", "MT2")]
public sealed class DeviceAndBatteryTests
{
    [Fact]
    public void TrackpadIsDetected()
    {
        if (!HardwareGate.Enabled) return;

        IReadOnlyList<TrackpadDevice> devices = TrackpadDeviceLocator.FindAll();

        Assert.NotEmpty(devices);
        Assert.All(devices, device =>
            Assert.NotEqual(TrackpadTransport.None, device.Transport));
        Assert.NotNull(TrackpadDeviceLocator.FindPreferred());
    }

    [Fact]
    public void BatteryLevelIsInRange()
    {
        if (!HardwareGate.Enabled) return;

        var reader = new TrackpadBatteryReader(new NamedPipeControlPanelPipe());
        BatteryReading reading = reader.Read();

        Assert.InRange(reading.Level, 0, 100);
        Assert.NotEqual(TrackpadTransport.None, reading.Transport);
    }

    [Fact]
    public void MissingDeviceReportsNoTransport()
    {
        if (!HardwareGate.Enabled) return;

        // The monitor must expose the current snapshot even before Start.
        using var monitor = new TrackpadDeviceMonitor();
        monitor.Poll();

        Assert.Equal(
            TrackpadDeviceLocator.FindPreferred()?.Transport ?? TrackpadTransport.None,
            monitor.Current.Transport);
    }
}
