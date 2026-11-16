namespace OpenTrackpad.Interop.Input;

public interface IMouseInjector
{
    void Move(int dx, int dy);
    void LeftDown();
    void LeftUp();
    bool IsLeftButtonDown();
}

public sealed class SendInputMouseInjector : IMouseInjector
{
    public void Move(int dx, int dy)
    {
        if (dx == 0 && dy == 0) return;
        Send(SendInputNativeMethods.MOUSEEVENTF_MOVE, dx, dy);
    }

    public void LeftDown() => Send(SendInputNativeMethods.MOUSEEVENTF_LEFTDOWN, 0, 0);

    public void LeftUp() => Send(SendInputNativeMethods.MOUSEEVENTF_LEFTUP, 0, 0);

    public bool IsLeftButtonDown() =>
        (SendInputNativeMethods.GetAsyncKeyState(SendInputNativeMethods.VK_LBUTTON) & 0x8000) != 0;

    private static void Send(uint flags, int dx, int dy)
    {
        var input = new SendInputNativeMethods.INPUT
        {
            type = SendInputNativeMethods.INPUT_MOUSE,
            U = new SendInputNativeMethods.InputUnion
            {
                mi = new SendInputNativeMethods.MOUSEINPUT
                {
                    dx = dx,
                    dy = dy,
                    dwFlags = flags,
                },
            },
        };

        uint sent = SendInputNativeMethods.SendInput(
            1, new[] { input }, System.Runtime.InteropServices.Marshal.SizeOf<SendInputNativeMethods.INPUT>());
        if (sent != 1)
            throw new System.ComponentModel.Win32Exception(
                System.Runtime.InteropServices.Marshal.GetLastWin32Error(), "SendInput failed");
    }
}
