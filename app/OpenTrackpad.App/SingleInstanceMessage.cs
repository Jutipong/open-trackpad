using System.Runtime.InteropServices;

namespace OpenTrackpad.App;

/// <summary>
/// Cross-process activation that also works when the running instance is
/// elevated. A named event created by an elevated process cannot be written to
/// by a non-elevated process, but a window message explicitly allowed through
/// UIPI can.
/// </summary>
internal static class SingleInstanceMessage
{
    private const int HwndBroadcast = 0xFFFF;
    private const uint MsgfltAllow = 1;

    public static uint Value { get; } = RegisterWindowMessage("OpenTrackpad.ShowWindow");

    public static void Broadcast()
    {
        if (Value == 0) return;

        for (int attempt = 0; attempt < 3; attempt++)
        {
            PostMessage(HwndBroadcast, Value, IntPtr.Zero, IntPtr.Zero);
            Thread.Sleep(100);
        }
    }

    public static void Allow(IntPtr hwnd)
    {
        if (Value == 0) return;
        ChangeWindowMessageFilterEx(hwnd, Value, MsgfltAllow, IntPtr.Zero);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint RegisterWindowMessage(string message);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool ChangeWindowMessageFilterEx(
        IntPtr hwnd, uint message, uint action, IntPtr changeFilterStruct);
}
