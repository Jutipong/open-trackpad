using OpenTrackpad.Interop.Driver;

namespace OpenTrackpad.Interop.RawInput;

public sealed record TrackpadDevice(
    string Path,
    string InstanceId,
    TrackpadTransport Transport);

/// <summary>
/// Finds the Magic Trackpad 2 PTP collection over USB or Bluetooth by its raw
/// input device path. Paths differ by transport:
/// USB: HID#VID_05AC&amp;PID_0265&amp;MI_01&amp;COL01#...
/// BT:  HID#{00001124-...}_VID&amp;0001004c_PID&amp;0265&amp;Col01&amp;Col01#...
/// </summary>
public static class TrackpadDeviceLocator
{
    public static IReadOnlyList<TrackpadDevice> FindAll()
    {
        var result = new List<TrackpadDevice>();
        foreach (RawInputDeviceInfo device in RawInputDeviceCatalog.List())
        {
            if (device.Type != RawInputNativeMethods.RIM_TYPEHID) continue;
            if (!TryClassify(device.Name, out TrackpadTransport transport)) continue;

            string? instanceId = ToDeviceInstanceId(device.Name);
            if (instanceId is null) continue;

            result.Add(new TrackpadDevice(device.Name, instanceId, transport));
        }

        return result
            .OrderBy(device => device.Transport == TrackpadTransport.Usb ? 0 : 1)
            .ToList();
    }

    public static TrackpadDevice? FindPreferred() => FindAll().FirstOrDefault();

    public static bool IsPtpCollectionPath(string path) => TryClassify(path, out _);

    public static bool TryClassify(string path, out TrackpadTransport transport)
    {
        transport = TrackpadTransport.None;
        if (string.IsNullOrWhiteSpace(path)) return false;

        bool isMagicTrackpad2 =
            path.Contains("vid_05ac&pid_0265", StringComparison.OrdinalIgnoreCase) ||
            path.Contains("vid&0001004c_pid&0265", StringComparison.OrdinalIgnoreCase);
        if (!isMagicTrackpad2) return false;

        if (path.Contains("mi_01&col01", StringComparison.OrdinalIgnoreCase))
        {
            transport = TrackpadTransport.Usb;
            return true;
        }

        if (path.Contains("col01&col01", StringComparison.OrdinalIgnoreCase))
        {
            transport = TrackpadTransport.Bluetooth;
            return true;
        }

        return false;
    }

    public static string? ToDeviceInstanceId(string interfacePath)
    {
        string path = interfacePath;
        if (path.StartsWith(@"\\?\", StringComparison.Ordinal)) path = path[4..];
        else if (path.StartsWith(@"\\.\", StringComparison.Ordinal)) path = path[4..];

        string[] parts = path.Split('#');
        if (parts.Length < 3) return null;
        if (!parts[0].Equals("HID", StringComparison.OrdinalIgnoreCase)) return null;

        return $"{parts[0]}\\{parts[1]}\\{parts[2]}";
    }
}
