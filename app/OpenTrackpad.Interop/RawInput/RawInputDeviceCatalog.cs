using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace OpenTrackpad.Interop.RawInput;

public sealed record RawInputDeviceInfo(IntPtr Handle, string Name, uint Type);

/// <summary>
/// Enumerates raw input devices so the app can find the PTP collection over
/// either USB or Bluetooth without opening the HID device.
/// </summary>
public static class RawInputDeviceCatalog
{
    public static IReadOnlyList<RawInputDeviceInfo> List()
    {
        uint count = 0;
        uint size = (uint)Marshal.SizeOf<RawInputNativeMethods.RAWINPUTDEVICELIST>();
        if (RawInputNativeMethods.GetRawInputDeviceList(null, ref count, size) == uint.MaxValue)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "GetRawInputDeviceList failed");

        var devices = new RawInputNativeMethods.RAWINPUTDEVICELIST[count];
        if (RawInputNativeMethods.GetRawInputDeviceList(devices, ref count, size) == uint.MaxValue)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "GetRawInputDeviceList failed");

        var result = new List<RawInputDeviceInfo>((int)count);
        for (int i = 0; i < count; i++)
        {
            string name = GetDeviceName(devices[i].hDevice);
            result.Add(new RawInputDeviceInfo(devices[i].hDevice, name, devices[i].dwType));
        }

        return result;
    }

    private static string GetDeviceName(IntPtr device)
    {
        uint size = 0;
        RawInputNativeMethods.GetRawInputDeviceInfoW(
            device, RawInputNativeMethods.RIDI_DEVICENAME, IntPtr.Zero, ref size);
        if (size == 0) return string.Empty;

        var builder = new StringBuilder((int)size);
        RawInputNativeMethods.GetRawInputDeviceInfoW(
            device, RawInputNativeMethods.RIDI_DEVICENAME, builder, ref size);
        return builder.ToString();
    }
}
