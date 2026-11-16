using System.Runtime.InteropServices;

namespace OpenTrackpad.Interop.Input;

public static class MouseSpeed
{
    private const uint SpiGetMouseSpeed = 0x0070;
    private const uint SpiSetMouseSpeed = 0x0071;
    private const uint SpifUpdateIniFile = 0x01;
    private const uint SpifSendChange = 0x02;

    public const int Min = 1;
    public const int Max = 20;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SystemParametersInfo(
        uint uiAction, uint uiParam, ref int pvParam, uint fWinIni);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SystemParametersInfo(
        uint uiAction, uint uiParam, IntPtr pvParam, uint fWinIni);

    public static int Get()
    {
        int value = 10;
        if (!SystemParametersInfo(SpiGetMouseSpeed, 0, ref value, 0))
            return 10;
        return Math.Clamp(value, Min, Max);
    }

    public static bool Set(int speed)
    {
        speed = Math.Clamp(speed, Min, Max);
        return SystemParametersInfo(
            SpiSetMouseSpeed,
            0,
            new IntPtr(speed),
            SpifUpdateIniFile | SpifSendChange);
    }
}
