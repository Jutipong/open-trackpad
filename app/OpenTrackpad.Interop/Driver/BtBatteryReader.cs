using System.Runtime.InteropServices;

namespace OpenTrackpad.Interop.Driver;

/// <summary>
/// Reads the battery level the Bluetooth filter publishes as the
/// System.Devices.BatteryLife device property. The property lives on the filter
/// device, which is the parent of the PTP HID collection.
/// </summary>
public static class BtBatteryReader
{
    private static readonly Guid BatteryLifePropertyGuid =
        new("49cd1f76-5626-4b17-a4e8-18b4aa1a2213");
    private const uint BatteryLifePropertyId = 10;
    private const uint DevPropTypeByte = 0x00000003;
    private const int CrSuccess = 0;

    public static bool TryRead(string deviceInstanceId, out int level)
    {
        level = 0;
        if (string.IsNullOrWhiteSpace(deviceInstanceId)) return false;
        if (CmNativeMethods.CM_Locate_DevNodeW(
                out uint devNode, deviceInstanceId, 0) != CrSuccess)
        {
            return false;
        }

        if (TryReadNode(devNode, out level)) return true;
        if (CmNativeMethods.CM_Get_Parent(out uint parent, devNode, 0) != CrSuccess)
            return false;

        return TryReadNode(parent, out level);
    }

    private static bool TryReadNode(uint devNode, out int level)
    {
        level = 0;
        var key = new DevPropKey
        {
            FormatId = BatteryLifePropertyGuid,
            PropertyId = BatteryLifePropertyId,
        };

        uint size = sizeof(byte);
        var buffer = new byte[size];
        int result = CmNativeMethods.CM_Get_DevNode_PropertyW(
            devNode, ref key, out uint propertyType, buffer, ref size, 0);

        if (result != CrSuccess || size < 1 || propertyType != DevPropTypeByte)
            return false;

        level = buffer[0];
        return level is >= 0 and <= 100;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DevPropKey
    {
        public Guid FormatId;
        public uint PropertyId;
    }

    private static class CmNativeMethods
    {
        [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
        public static extern int CM_Locate_DevNodeW(
            out uint devNode, string deviceId, uint flags);

        [DllImport("cfgmgr32.dll")]
        public static extern int CM_Get_Parent(
            out uint parent, uint devNode, uint flags);

        [DllImport("cfgmgr32.dll")]
        public static extern int CM_Get_DevNode_PropertyW(
            uint devNode,
            ref DevPropKey propertyKey,
            out uint propertyType,
            [Out] byte[]? propertyBuffer,
            ref uint propertyBufferSize,
            uint flags);
    }
}
