using Microsoft.Win32;
using OpenTrackpad.Interop.Driver;

namespace OpenTrackpad.App;

public static class DriverHealth
{
    private const string UsbServiceKey =
        @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\WUDF\Services\AmtPtpDeviceUsbUm";

    private const string BluetoothServiceKey =
        @"SYSTEM\CurrentControlSet\Services\AmtPtpHidFilter";

    public static bool IsDriverInstalled() =>
        KeyExists(UsbServiceKey) || KeyExists(BluetoothServiceKey);

    public static bool IsAccessAllowed(string parametersKey) =>
        ReadDword(parametersKey, "AllowNonAdminAccess") != 0;

    private static bool KeyExists(string path)
    {
        try
        {
            using RegistryKey? key = Registry.LocalMachine.OpenSubKey(path, writable: false);
            return key is not null;
        }
        catch
        {
            return false;
        }
    }

    private static uint ReadDword(string path, string name)
    {
        try
        {
            using RegistryKey? key = Registry.LocalMachine.OpenSubKey(path, writable: false);
            return key?.GetValue(name) switch
            {
                int value => unchecked((uint)value),
                uint value => value,
                long value => unchecked((uint)value),
                _ => 0,
            };
        }
        catch
        {
            return 0;
        }
    }
}
