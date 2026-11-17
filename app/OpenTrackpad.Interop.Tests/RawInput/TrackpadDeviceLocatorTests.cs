using OpenTrackpad.Interop.Driver;
using OpenTrackpad.Interop.RawInput;

namespace OpenTrackpad.Interop.Tests.RawInput;

public sealed class TrackpadDeviceLocatorTests
{
    private const string UsbPath =
        @"\\?\HID#VID_05AC&PID_0265&MI_01&COL01#8&10a09981&0&0000#{4d1e55b2-f16f-11cf-88cb-001111000030}";

    private const string BluetoothPath =
        @"\\?\HID#{00001124-0000-1000-8000-00805f9b34fb}_VID&0001004c_PID&0265&Col01&Col01#a&56ebb6f&0&0000#{4d1e55b2-f16f-11cf-88cb-001111000030}";

    [Fact]
    public void ClassifiesUsbPtpCollection()
    {
        Assert.True(TrackpadDeviceLocator.TryClassify(UsbPath, out TrackpadTransport transport));
        Assert.Equal(TrackpadTransport.Usb, transport);
    }

    [Fact]
    public void ClassifiesBluetoothPtpCollection()
    {
        Assert.True(TrackpadDeviceLocator.TryClassify(BluetoothPath, out TrackpadTransport transport));
        Assert.Equal(TrackpadTransport.Bluetooth, transport);
    }

    [Theory]
    [InlineData(@"\\?\HID#{00001124-0000-1000-8000-00805f9b34fb}_VID&0001004c_PID&0265&Col02#9&19de055f&0&0001#{4d1e55b2-f16f-11cf-88cb-001111000030}")]
    [InlineData(@"\\?\HID#VID_05AC&PID_0265&MI_02#8&3477d743&0&0000#{4d1e55b2-f16f-11cf-88cb-001111000030}")]
    [InlineData(@"\\?\HID#ELAN260A&Col03#4&39e3071&0&0002#{4d1e55b2-f16f-11cf-88cb-001111000030}")]
    [InlineData(@"\\?\HID#VID_05AC&PID_0324&MI_01&COL01#8&10a09981&0&0000#{4d1e55b2-f16f-11cf-88cb-001111000030}")]
    public void RejectsOtherHidCollections(string path)
    {
        Assert.False(TrackpadDeviceLocator.TryClassify(path, out TrackpadTransport transport));
        Assert.Equal(TrackpadTransport.None, transport);
    }

    [Fact]
    public void ConvertsBluetoothInterfacePathToDeviceInstanceId()
    {
        string? instanceId = TrackpadDeviceLocator.ToDeviceInstanceId(BluetoothPath);

        Assert.Equal(
            @"HID\{00001124-0000-1000-8000-00805f9b34fb}_VID&0001004c_PID&0265&Col01&Col01\a&56ebb6f&0&0000",
            instanceId);
    }

    [Fact]
    public void ConvertsUsbInterfacePathToDeviceInstanceId()
    {
        string? instanceId = TrackpadDeviceLocator.ToDeviceInstanceId(UsbPath);

        Assert.Equal(
            @"HID\VID_05AC&PID_0265&MI_01&COL01\8&10a09981&0&0000",
            instanceId);
    }

    [Fact]
    public void ConvertsDosDevicePathToDeviceInstanceId()
    {
        string path = UsbPath.Replace(@"\\?\", @"\\.\", StringComparison.Ordinal);

        string? instanceId = TrackpadDeviceLocator.ToDeviceInstanceId(path);

        Assert.Equal(
            @"HID\VID_05AC&PID_0265&MI_01&COL01\8&10a09981&0&0000",
            instanceId);
    }

    [Theory]
    [InlineData("")]
    [InlineData(@"\\?\USB#VID_05AC&PID_0265#1234")]
    [InlineData(@"\\?\HID#only-two-parts")]
    public void RejectsPathsWithoutHidInstance(string path)
    {
        Assert.Null(TrackpadDeviceLocator.ToDeviceInstanceId(path));
    }
}
