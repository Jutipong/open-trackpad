using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace OpenTrackpad.App;

internal static class DarkTitleBar
{
    // DWMWA_USE_IMMERSIVE_DARK_MODE is 20 on Windows 10 20H1+ and 19 on earlier builds.
    private const int ImmersiveDarkMode = 20;
    private const int ImmersiveDarkModeLegacy = 19;

    public static void Apply(Window window)
    {
        try
        {
            IntPtr handle = new WindowInteropHelper(window).Handle;
            int enabled = 1;
            if (DwmSetWindowAttribute(handle, ImmersiveDarkMode, ref enabled, sizeof(int)) != 0)
                DwmSetWindowAttribute(handle, ImmersiveDarkModeLegacy, ref enabled, sizeof(int));
        }
        catch
        {
            // Dark title bar is cosmetic and unsupported on older Windows builds.
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
