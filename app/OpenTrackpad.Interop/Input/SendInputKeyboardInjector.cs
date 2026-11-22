using System.ComponentModel;
using System.Runtime.InteropServices;
using OpenTrackpad.Core.Input;

namespace OpenTrackpad.Interop.Input;

public interface IKeyboardInjector
{
    void SendShortcut(IReadOnlyList<ushort> virtualKeys);
}

public sealed class SendInputKeyboardInjector : IKeyboardInjector
{
    public void SendShortcut(IReadOnlyList<ushort> virtualKeys)
    {
        ArgumentNullException.ThrowIfNull(virtualKeys);
        if (virtualKeys.Count == 0) return;

        var inputs = new SendInputNativeMethods.INPUT[virtualKeys.Count * 2];
        for (int i = 0; i < virtualKeys.Count; i++)
        {
            inputs[i] = CreateKey(virtualKeys[i], keyUp: false);
            inputs[inputs.Length - 1 - i] = CreateKey(virtualKeys[i], keyUp: true);
        }

        uint sent = SendInputNativeMethods.SendInput(
            (uint)inputs.Length,
            inputs,
            Marshal.SizeOf<SendInputNativeMethods.INPUT>());
        if (sent != (uint)inputs.Length)
        {
            int error = Marshal.GetLastWin32Error();
            ReleaseAll(virtualKeys);
            throw new Win32Exception(error, "SendInput keyboard failed");
        }
    }

    private static void ReleaseAll(IReadOnlyList<ushort> virtualKeys)
    {
        var releases = new SendInputNativeMethods.INPUT[virtualKeys.Count];
        for (int i = 0; i < virtualKeys.Count; i++)
            releases[i] = CreateKey(virtualKeys[i], keyUp: true);

        try
        {
            SendInputNativeMethods.SendInput(
                (uint)releases.Length,
                releases,
                Marshal.SizeOf<SendInputNativeMethods.INPUT>());
        }
        catch
        {
            // Best effort only; the original failure is more useful.
        }
    }

    private static SendInputNativeMethods.INPUT CreateKey(ushort virtualKey, bool keyUp) =>
        new()
        {
            type = SendInputNativeMethods.INPUT_KEYBOARD,
            U = new SendInputNativeMethods.InputUnion
            {
                ki = new SendInputNativeMethods.KEYBDINPUT
                {
                    wVk = virtualKey,
                    dwFlags = keyUp ? SendInputNativeMethods.KEYEVENTF_KEYUP : 0,
                    dwExtraInfo = SendInputNativeMethods.InjectedExtraInfo,
                },
            },
        };
}

public static class SwipeGestureShortcuts
{
    private const ushort VkCtrl = 0x11;
    private const ushort VkAlt = 0x12;
    private const ushort VkWin = 0x5B;
    private const ushort VkC = 0x43;
    private const ushort VkV = 0x56;
    private const ushort VkW = 0x57;
    private const ushort VkF4 = 0x73;
    private const ushort VkLeft = 0x25;
    private const ushort VkRight = 0x27;

    public static IReadOnlyList<ushort>? For(SwipeGesture gesture) =>
        (gesture.ContactCount, gesture.Direction) switch
        {
            (4, SwipeDirection.Down) => new[] { VkCtrl, VkC },
            (4, SwipeDirection.Up) => new[] { VkCtrl, VkV },
            (4, SwipeDirection.Left) => new[] { VkAlt, VkF4 },
            (4, SwipeDirection.Right) => new[] { VkCtrl, VkW },
            (3, SwipeDirection.Left) => new[] { VkCtrl, VkWin, VkRight },
            (3, SwipeDirection.Right) => new[] { VkCtrl, VkWin, VkLeft },
            _ => null,
        };

    public static string Describe(SwipeGesture gesture) =>
        (gesture.ContactCount, gesture.Direction) switch
        {
            (4, SwipeDirection.Down) => "Copy (Ctrl+C)",
            (4, SwipeDirection.Up) => "Paste (Ctrl+V)",
            (4, SwipeDirection.Left) => "Close app (Alt+F4)",
            (4, SwipeDirection.Right) => "Close tab (Ctrl+W)",
            (3, SwipeDirection.Left) => "Next desktop (Ctrl+Win+Right)",
            (3, SwipeDirection.Right) => "Previous desktop (Ctrl+Win+Left)",
            _ => string.Empty,
        };
}
