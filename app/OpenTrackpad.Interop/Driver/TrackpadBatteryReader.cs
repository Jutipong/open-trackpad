using OpenTrackpad.Interop.RawInput;

namespace OpenTrackpad.Interop.Driver;

public readonly record struct BatteryReading(int Level, TrackpadTransport Transport);

/// <summary>
/// Reads battery over USB (driver pipe) or Bluetooth (device property, with
/// the administrator-only IOCTL as a fallback).
/// </summary>
public sealed class TrackpadBatteryReader
{
    private readonly IControlPanelPipe _usb;

    public TrackpadBatteryReader(IControlPanelPipe usb) => _usb = usb;

    public BatteryReading Read()
    {
        IReadOnlyList<TrackpadDevice> devices = TrackpadDeviceLocator.FindAll();
        TrackpadDevice? usb = devices.FirstOrDefault(
            d => d.Transport == TrackpadTransport.Usb);
        TrackpadDevice? bluetooth = devices.FirstOrDefault(
            d => d.Transport == TrackpadTransport.Bluetooth);

        Exception? error = null;

        if (usb is not null)
        {
            try
            {
                ControlPanelResponse response = _usb.GetBattery();
                if (response.Ok)
                    return new BatteryReading(response.Value, TrackpadTransport.Usb);
                error = new IOException($"USB battery status 0x{response.Status:X2}");
            }
            catch (Exception ex)
            {
                error = ex;
            }
        }

        if (bluetooth is not null)
        {
            if (BtBatteryReader.TryRead(bluetooth.InstanceId, out int level))
                return new BatteryReading(level, TrackpadTransport.Bluetooth);

            try
            {
                ControlPanelResponse response = new BtControlDevice().GetBattery();
                if (response.Ok)
                    return new BatteryReading(response.Value, TrackpadTransport.Bluetooth);
                error ??= new IOException($"Bluetooth battery status 0x{response.Status:X2}");
            }
            catch (Exception ex)
            {
                error ??= ex;
            }
        }

        throw new IOException("ไม่พบ Magic Trackpad 2 ที่เชื่อมต่อผ่าน USB หรือ Bluetooth", error);
    }
}
