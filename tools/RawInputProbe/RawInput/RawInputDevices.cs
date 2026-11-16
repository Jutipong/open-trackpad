using RawInputProbe.Native;
using System.Text;

namespace RawInputProbe.RawInput;

public sealed record RawDevice(IntPtr Handle, string Name, uint Type)
{
    public string TypeName => Type switch
    {
        0 => "mouse",
        1 => "keyboard",
        2 => "hid",
        _ => "?",
    };
}

public static class RawInputDevices
{
    public static List<RawDevice> Enumerate()
    {
        uint count = 0;
        uint size = (uint)System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.RAWINPUTDEVICELIST>();
        if (NativeMethods.GetRawInputDeviceList(null, ref count, size) == uint.MaxValue)
            throw new System.ComponentModel.Win32Exception();

        var list = new NativeMethods.RAWINPUTDEVICELIST[count];
        if (NativeMethods.GetRawInputDeviceList(list, ref count, size) == uint.MaxValue)
            throw new System.ComponentModel.Win32Exception();

        var result = new List<RawDevice>((int)count);
        foreach (var entry in list.Take((int)count))
        {
            uint nameSize = 0;
            NativeMethods.GetRawInputDeviceInfoW(entry.hDevice, NativeMethods.RIDI_DEVICENAME, IntPtr.Zero, ref nameSize);
            var name = new StringBuilder((int)Math.Max(1, nameSize));
            NativeMethods.GetRawInputDeviceInfoW(entry.hDevice, NativeMethods.RIDI_DEVICENAME, name, ref nameSize);
            result.Add(new RawDevice(entry.hDevice, name.ToString(), entry.dwType));
        }
        return result;
    }

    public static string? FindMagicTrackpadTouchpad()
    {
        var devices = Enumerate();
        return devices.FirstOrDefault(d => IsMagicTrackpadTouchpad(d.Name))?.Name
            ?? devices.FirstOrDefault(d =>
                d.Name.Contains("vid_05ac&pid_0265", StringComparison.OrdinalIgnoreCase) &&
                d.Name.Contains("mi_01", StringComparison.OrdinalIgnoreCase))?.Name;
    }

    public static bool IsMagicTrackpadTouchpad(string name) =>
        name.Contains("vid_05ac&pid_0265", StringComparison.OrdinalIgnoreCase) &&
        name.Contains("mi_01&col01", StringComparison.OrdinalIgnoreCase);
}
