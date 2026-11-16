using System.ComponentModel;
using RawInputProbe.Native;

namespace RawInputProbe.Input;

public static class MouseInjector
{
    public static void Move(int dx, int dy)
    {
        if (dx == 0 && dy == 0) return;
        Send(NativeMethods.MOUSEEVENTF_MOVE, dx, dy);
    }

    public static void LeftDown() => Send(NativeMethods.MOUSEEVENTF_LEFTDOWN, 0, 0);

    public static void LeftUp() => Send(NativeMethods.MOUSEEVENTF_LEFTUP, 0, 0);

    public static bool IsLeftButtonDown() =>
        (NativeMethods.GetAsyncKeyState(NativeMethods.VK_LBUTTON) & 0x8000) != 0;

    private static void Send(uint flags, int dx, int dy)
    {
        var input = new NativeMethods.INPUT
        {
            type = NativeMethods.INPUT_MOUSE,
            U = new NativeMethods.InputUnion
            {
                mi = new NativeMethods.MOUSEINPUT
                {
                    dx = dx,
                    dy = dy,
                    dwFlags = flags,
                },
            },
        };

        uint sent = NativeMethods.SendInput(
            1, new[] { input }, System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.INPUT>());
        if (sent != 1)
            throw new Win32Exception(System.Runtime.InteropServices.Marshal.GetLastWin32Error(),
                "SendInput failed");
    }
}
