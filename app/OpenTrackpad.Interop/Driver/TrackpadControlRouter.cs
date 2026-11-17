using OpenTrackpad.Interop.RawInput;

namespace OpenTrackpad.Interop.Driver;

/// <summary>
/// Routes driver commands to whichever transports are currently present.
/// USB is tried first so a docked trackpad keeps using the existing pipe.
/// </summary>
public sealed class TrackpadControlRouter
{
    private readonly NamedPipeControlPanelPipe _usb;
    private readonly BtControlDevice _bluetooth;

    public TrackpadControlRouter(
        NamedPipeControlPanelPipe usb, BtControlDevice? bluetooth = null)
    {
        _usb = usb;
        _bluetooth = bluetooth ?? new BtControlDevice();
    }

    public ControlPanelResponse ReloadSettings()
    {
        return RunAll((device) => device.Transport == TrackpadTransport.Usb
            ? _usb.ReloadSettings()
            : _bluetooth.ReloadSettings());
    }

    public ControlPanelResponse Actuate(uint strength)
    {
        IReadOnlyList<TrackpadDevice> devices = TrackpadDeviceLocator.FindAll();
        Exception? lastError = null;

        foreach (TrackpadDevice device in devices)
        {
            try
            {
                ControlPanelResponse response = device.Transport == TrackpadTransport.Usb
                    ? _usb.Actuate(strength)
                    : _bluetooth.Actuate(strength);
                if (response.Ok) return response;
            }
            catch (Exception ex)
            {
                lastError = ex;
            }
        }

        if (lastError is not null)
            throw new IOException("สั่ง haptic ผ่านไดรเวอร์ไม่สำเร็จ", lastError);

        return new ControlPanelResponse(ControlPanelProtocol.StatusFail, 0);
    }

    public TrackpadTransport DetectTransport() =>
        TrackpadDeviceLocator.FindPreferred()?.Transport ?? TrackpadTransport.None;

    private static ControlPanelResponse RunAll(
        Func<TrackpadDevice, ControlPanelResponse> action)
    {
        IReadOnlyList<TrackpadDevice> devices = TrackpadDeviceLocator.FindAll();
        Exception? lastError = null;
        bool succeeded = false;

        foreach (TrackpadDevice device in devices)
        {
            try
            {
                if (action(device).Ok) succeeded = true;
            }
            catch (Exception ex)
            {
                lastError = ex;
            }
        }

        if (succeeded)
            return new ControlPanelResponse(ControlPanelProtocol.StatusOk, 0);

        if (lastError is not null)
            throw new IOException("reload ค่าไดรเวอร์ไม่สำเร็จ", lastError);

        return new ControlPanelResponse(ControlPanelProtocol.StatusFail, 0);
    }
}
