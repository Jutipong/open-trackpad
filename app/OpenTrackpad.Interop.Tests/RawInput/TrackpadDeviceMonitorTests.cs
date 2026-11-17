using OpenTrackpad.Interop.Driver;
using OpenTrackpad.Interop.RawInput;

namespace OpenTrackpad.Interop.Tests.RawInput;

public sealed class TrackpadDeviceMonitorTests
{
    private const string UsbPath =
        @"\\?\HID#VID_05AC&PID_0265&MI_01&COL01#8&10a09981&0&0000#{4d1e55b2-f16f-11cf-88cb-001111000030}";

    private const string BluetoothPath =
        @"\\?\HID#{00001124-0000-1000-8000-00805f9b34fb}_VID&0001004c_PID&0265&Col01&Col01#a&56ebb6f&0&0000#{4d1e55b2-f16f-11cf-88cb-001111000030}";

    private static readonly TrackpadDevice Usb =
        new(UsbPath, @"HID\VID_05AC&PID_0265&MI_01&COL01\8&10a09981&0&0000", TrackpadTransport.Usb);

    private static readonly TrackpadDevice Bluetooth =
        new(BluetoothPath, @"HID\{00001124-0000-1000-8000-00805f9b34fb}_VID&0001004c_PID&0265&Col01&Col01\a&56ebb6f&0&0000",
            TrackpadTransport.Bluetooth);

    [Fact]
    public void FirstPollRaisesChange()
    {
        using var monitor = new TrackpadDeviceMonitor(() => new[] { Usb });
        var changes = new List<TrackpadDeviceChange>();
        monitor.Changed += changes.Add;

        monitor.Poll();

        Assert.Single(changes);
        Assert.True(changes[0].IsConnected);
        Assert.Equal(TrackpadTransport.Usb, changes[0].Transport);
    }

    [Fact]
    public void UnchangedSnapshotDoesNotRaiseAgain()
    {
        using var monitor = new TrackpadDeviceMonitor(() => new[] { Usb });
        int count = 0;
        monitor.Changed += _ => count++;

        monitor.Poll();
        monitor.Poll();

        Assert.Equal(1, count);
    }

    [Fact]
    public void ReorderedSnapshotDoesNotRaiseAgain()
    {
        var devices = new[] { Usb, Bluetooth };
        using var monitor = new TrackpadDeviceMonitor(() => devices);
        int count = 0;
        monitor.Changed += _ => count++;

        monitor.Poll();
        Array.Reverse(devices);
        monitor.Poll();

        Assert.Equal(1, count);
    }

    [Fact]
    public void PrefersUsbWhenBothTransportsArePresent()
    {
        using var monitor = new TrackpadDeviceMonitor(() => new[] { Bluetooth, Usb });
        monitor.Poll();

        Assert.Equal(TrackpadTransport.Usb, monitor.Current.Transport);
        Assert.Equal(2, monitor.Current.Devices.Count);
    }

    [Fact]
    public void TransportSwitchRaisesChange()
    {
        TrackpadDevice? device = Usb;
        using var monitor = new TrackpadDeviceMonitor(() => device is null ? Array.Empty<TrackpadDevice>() : new[] { device });
        var changes = new List<TrackpadDeviceChange>();
        monitor.Changed += changes.Add;

        monitor.Poll();
        device = Bluetooth;
        monitor.Poll();

        Assert.Equal(2, changes.Count);
        Assert.Equal(TrackpadTransport.Bluetooth, changes[1].Transport);
    }

    [Fact]
    public void DisconnectRaisesEmptyChange()
    {
        TrackpadDevice? device = Usb;
        using var monitor = new TrackpadDeviceMonitor(() => device is null ? Array.Empty<TrackpadDevice>() : new[] { device });
        var changes = new List<TrackpadDeviceChange>();
        monitor.Changed += changes.Add;

        monitor.Poll();
        device = null;
        monitor.Poll();

        Assert.Equal(2, changes.Count);
        Assert.False(changes[1].IsConnected);
        Assert.Equal(TrackpadTransport.None, changes[1].Transport);
    }

    [Fact]
    public void ProbeFailureRaisesErrorWithoutChange()
    {
        using var monitor = new TrackpadDeviceMonitor(
            () => throw new InvalidOperationException("probe failed"));
        var errors = new List<Exception>();
        monitor.Changed += _ => throw new Xunit.Sdk.XunitException("Changed should not fire");
        monitor.Error += errors.Add;

        monitor.Poll();

        Assert.Single(errors);
    }

    [Fact]
    public void SubscriberFailureIsReportedAsError()
    {
        using var monitor = new TrackpadDeviceMonitor(() => new[] { Usb });
        var errors = new List<Exception>();
        monitor.Changed += _ => throw new InvalidOperationException("subscriber failed");
        monitor.Error += errors.Add;

        monitor.Poll();

        Assert.Single(errors);
    }

    [Fact]
    public void StartPollRaisesInitialSnapshot()
    {
        using var monitor = new TrackpadDeviceMonitor(
            () => new[] { Bluetooth }, TimeSpan.FromMinutes(1));
        var changes = new List<TrackpadDeviceChange>();
        monitor.Changed += changes.Add;

        monitor.Start();

        Assert.Single(changes);
        Assert.Equal(TrackpadTransport.Bluetooth, changes[0].Transport);
    }

    [Fact]
    public void PollAfterDisposeDoesNothing()
    {
        var monitor = new TrackpadDeviceMonitor(() => new[] { Usb });
        monitor.Dispose();

        monitor.Poll();

        Assert.False(monitor.Current.IsConnected);
    }
}
