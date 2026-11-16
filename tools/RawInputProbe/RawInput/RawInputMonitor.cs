using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using RawInputProbe.Hid;
using RawInputProbe.Native;
using RawInputProbe.Ptp;

namespace RawInputProbe.RawInput;

public sealed class RawInputMonitor
{
    private readonly Func<byte[], PtpFrame> _parse;
    private readonly string _expectedPathFragment;
    private readonly Action<PtpFrame> _onFrame;
    private readonly bool _debug;
    private readonly HashSet<string> _seenPaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly NativeMethods.WndProc _wndProc;
    private IntPtr _hwnd;
    private bool _registered;

    public RawInputMonitor(
        Func<byte[], PtpFrame> parse,
        string expectedPathFragment,
        Action<PtpFrame> onFrame,
        bool debug = false)
    {
        _parse = parse;
        _expectedPathFragment = expectedPathFragment;
        _onFrame = onFrame;
        _debug = debug;
        _wndProc = WindowProc;
    }

    public void Run(TimeSpan? duration)
    {
        string className = $"RawInputProbe_{Environment.ProcessId}";
        var wc = new NativeMethods.WNDCLASSEX
        {
            cbSize = (uint)Marshal.SizeOf<NativeMethods.WNDCLASSEX>(),
            lpfnWndProc = _wndProc,
            hInstance = NativeMethods.GetModuleHandleW(null),
            lpszClassName = className,
        };

        if (NativeMethods.RegisterClassExW(ref wc) == 0)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "RegisterClassEx failed");

        _hwnd = NativeMethods.CreateWindowExW(
            0, className, "RawInputProbe", 0, 0, 0, 0, 0,
            IntPtr.Zero, IntPtr.Zero, wc.hInstance, IntPtr.Zero);
        if (_hwnd == IntPtr.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateWindowEx failed");

        var registration = new NativeMethods.RAWINPUTDEVICE
        {
            usUsagePage = 0x0D, // Digitizers
            usUsage = 0x05,     // Touch Pad
            dwFlags = NativeMethods.RIDEV_INPUTSINK,
            hwndTarget = _hwnd,
        };

        if (!NativeMethods.RegisterRawInputDevices(
                new[] { registration }, 1,
                (uint)Marshal.SizeOf<NativeMethods.RAWINPUTDEVICE>()))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "RegisterRawInputDevices failed");
        }
        _registered = true;

        if (duration.HasValue)
            NativeMethods.SetTimer(_hwnd, 1, (uint)Math.Max(1, duration.Value.TotalMilliseconds), IntPtr.Zero);

        while (NativeMethods.GetMessageW(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            NativeMethods.TranslateMessage(ref msg);
            NativeMethods.DispatchMessageW(ref msg);
        }

        if (_registered)
        {
            // Unregister by targeting the window with RIDEV_REMOVE.
            registration.dwFlags = 0x00000001; // RIDEV_REMOVE
            registration.hwndTarget = IntPtr.Zero;
            NativeMethods.RegisterRawInputDevices(
                new[] { registration }, 1,
                (uint)Marshal.SizeOf<NativeMethods.RAWINPUTDEVICE>());
            _registered = false;
        }

        if (_hwnd != IntPtr.Zero)
        {
            NativeMethods.DestroyWindow(_hwnd);
            _hwnd = IntPtr.Zero;
        }
    }

    public void Stop()
    {
        if (_hwnd != IntPtr.Zero)
            NativeMethods.PostMessageW(_hwnd, NativeMethods.WM_APP_STOP, IntPtr.Zero, IntPtr.Zero);
    }

    private IntPtr WindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        switch (msg)
        {
            case NativeMethods.WM_INPUT:
                HandleRawInput(lParam);
                return IntPtr.Zero;

            case NativeMethods.WM_TIMER:
                NativeMethods.KillTimer(hWnd, wParam);
                NativeMethods.PostQuitMessage(0);
                return IntPtr.Zero;

            case NativeMethods.WM_APP_STOP:
                NativeMethods.PostQuitMessage(0);
                return IntPtr.Zero;

            case NativeMethods.WM_CLOSE:
                NativeMethods.DestroyWindow(hWnd);
                return IntPtr.Zero;

            case NativeMethods.WM_DESTROY:
                NativeMethods.PostQuitMessage(0);
                return IntPtr.Zero;

            default:
                return NativeMethods.DefWindowProcW(hWnd, msg, wParam, lParam);
        }
    }

    private void HandleRawInput(IntPtr rawInput)
    {
        uint size = 0;
        uint headerSize = (uint)Marshal.SizeOf<NativeMethods.RAWINPUTHEADER>();
        uint result = NativeMethods.GetRawInputData(
            rawInput, NativeMethods.RID_INPUT, IntPtr.Zero, ref size, headerSize);
        if (result == uint.MaxValue || size == 0) return;

        IntPtr buffer = Marshal.AllocHGlobal((int)size);
        try
        {
            result = NativeMethods.GetRawInputData(
                rawInput, NativeMethods.RID_INPUT, buffer, ref size, headerSize);
            if (result == uint.MaxValue) return;

            var header = Marshal.PtrToStructure<NativeMethods.RAWINPUTHEADER>(buffer);
            if (header.dwType != NativeMethods.RIM_TYPEHID) return;

            int hidOffset = Marshal.SizeOf<NativeMethods.RAWINPUTHEADER>();
            uint reportSize = (uint)Marshal.ReadInt32(buffer, hidOffset);
            uint reportCount = (uint)Marshal.ReadInt32(buffer, hidOffset + 4);
            IntPtr data = IntPtr.Add(buffer, hidOffset + 8);

            string deviceName = GetDeviceName(header.hDevice);
            if (_debug && _seenPaths.Add(deviceName))
            {
                Console.Error.WriteLine(
                    $"[raw] path={deviceName} type={header.dwType} count={reportCount} size={reportSize}");
            }
            if (_expectedPathFragment.Length > 0 &&
                !deviceName.Contains(_expectedPathFragment, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            for (uint i = 0; i < reportCount; i++)
            {
                int offset = checked((int)(i * reportSize));
                var raw = new byte[reportSize];
                Marshal.Copy(IntPtr.Add(data, offset), raw, 0, raw.Length);
                try
                {
                    var frame = _parse(raw);
                    _onFrame(frame);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[parse] {ex.Message} raw={Convert.ToHexString(raw)}");
                }
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static string GetDeviceName(IntPtr device)
    {
        uint size = 0;
        NativeMethods.GetRawInputDeviceInfoW(device, NativeMethods.RIDI_DEVICENAME, IntPtr.Zero, ref size);
        if (size == 0) return string.Empty;

        var builder = new StringBuilder((int)size);
        NativeMethods.GetRawInputDeviceInfoW(device, NativeMethods.RIDI_DEVICENAME, builder, ref size);
        return builder.ToString();
    }
}
