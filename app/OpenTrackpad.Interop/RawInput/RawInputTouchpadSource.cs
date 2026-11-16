using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace OpenTrackpad.Interop.RawInput;

/// <summary>
/// Receives HID input reports from the precision touchpad through Raw Input.
/// Owns a native hidden window and a dedicated message-loop thread so it does
/// not depend on WPF or the UI dispatcher.
/// </summary>
public sealed class RawInputTouchpadSource : IDisposable
{
    private readonly string _expectedPathFragment;
    private readonly ManualResetEventSlim _started = new(false);
    private readonly object _gate = new();
    private readonly RawInputNativeMethods.WndProc _wndProc;

    private Thread? _thread;
    private IntPtr _hwnd;
    private Exception? _startError;
    private volatile bool _disposed;

    public RawInputTouchpadSource(string expectedPathFragment = "mi_01&col01")
    {
        _expectedPathFragment = expectedPathFragment;
        _wndProc = WindowProc;
    }

    public event Action<byte[]>? ReportReceived;
    public event Action<Exception>? Error;

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_thread is { IsAlive: true }) return;

        _startError = null;
        _started.Reset();
        _thread = new Thread(ThreadMain)
        {
            IsBackground = true,
            Name = "OpenTrackpad.RawInput",
        };
        _thread.Start();

        if (!_started.Wait(TimeSpan.FromSeconds(5)))
            throw new TimeoutException("Raw Input window did not start.");
        if (_startError is not null)
            throw new InvalidOperationException("Raw Input registration failed.", _startError);
    }

    public void Stop()
    {
        IntPtr hwnd;
        Thread? thread;
        lock (_gate)
        {
            hwnd = _hwnd;
            thread = _thread;
        }

        if (hwnd != IntPtr.Zero)
            RawInputNativeMethods.PostMessageW(hwnd, RawInputNativeMethods.WM_APP_STOP, IntPtr.Zero, IntPtr.Zero);

        if (thread is { IsAlive: true } && Thread.CurrentThread != thread)
            thread.Join(TimeSpan.FromSeconds(3));
    }

    private void ThreadMain()
    {
        string className = $"OpenTrackpadRawInput_{Guid.NewGuid():N}";
        try
        {
            var wc = new RawInputNativeMethods.WNDCLASSEX
            {
                cbSize = (uint)Marshal.SizeOf<RawInputNativeMethods.WNDCLASSEX>(),
                lpfnWndProc = _wndProc,
                hInstance = RawInputNativeMethods.GetModuleHandleW(null),
                lpszClassName = className,
            };

            if (RawInputNativeMethods.RegisterClassExW(ref wc) == 0)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "RegisterClassEx failed");

            IntPtr hwnd = RawInputNativeMethods.CreateWindowExW(
                0, className, "OpenTrackpadRawInput", 0, 0, 0, 0, 0,
                IntPtr.Zero, IntPtr.Zero, wc.hInstance, IntPtr.Zero);
            if (hwnd == IntPtr.Zero)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateWindowEx failed");

            lock (_gate) _hwnd = hwnd;

            var registration = new RawInputNativeMethods.RAWINPUTDEVICE
            {
                usUsagePage = 0x0D,
                usUsage = 0x05,
                dwFlags = RawInputNativeMethods.RIDEV_INPUTSINK,
                hwndTarget = hwnd,
            };

            if (!RawInputNativeMethods.RegisterRawInputDevices(
                    new[] { registration }, 1,
                    (uint)Marshal.SizeOf<RawInputNativeMethods.RAWINPUTDEVICE>()))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "RegisterRawInputDevices failed");
            }

            _started.Set();

            while (RawInputNativeMethods.GetMessageW(out var msg, IntPtr.Zero, 0, 0) > 0)
            {
                RawInputNativeMethods.TranslateMessage(ref msg);
                RawInputNativeMethods.DispatchMessageW(ref msg);
            }

            registration.dwFlags = RawInputNativeMethods.RIDEV_REMOVE;
            registration.hwndTarget = IntPtr.Zero;
            RawInputNativeMethods.RegisterRawInputDevices(
                new[] { registration }, 1,
                (uint)Marshal.SizeOf<RawInputNativeMethods.RAWINPUTDEVICE>());
        }
        catch (Exception ex)
        {
            _startError = ex;
            _started.Set();
            Error?.Invoke(ex);
        }
        finally
        {
            lock (_gate)
            {
                if (_hwnd != IntPtr.Zero)
                {
                    RawInputNativeMethods.DestroyWindow(_hwnd);
                    _hwnd = IntPtr.Zero;
                }
            }
            _started.Set();
        }
    }

    private IntPtr WindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        switch (msg)
        {
            case RawInputNativeMethods.WM_INPUT:
                HandleRawInput(lParam);
                return IntPtr.Zero;

            case RawInputNativeMethods.WM_APP_STOP:
                RawInputNativeMethods.PostQuitMessage(0);
                return IntPtr.Zero;

            case RawInputNativeMethods.WM_CLOSE:
                RawInputNativeMethods.DestroyWindow(hWnd);
                return IntPtr.Zero;

            case RawInputNativeMethods.WM_DESTROY:
                RawInputNativeMethods.PostQuitMessage(0);
                return IntPtr.Zero;

            default:
                return RawInputNativeMethods.DefWindowProcW(hWnd, msg, wParam, lParam);
        }
    }

    private void HandleRawInput(IntPtr rawInput)
    {
        uint size = 0;
        uint headerSize = (uint)Marshal.SizeOf<RawInputNativeMethods.RAWINPUTHEADER>();
        uint result = RawInputNativeMethods.GetRawInputData(
            rawInput, RawInputNativeMethods.RID_INPUT, IntPtr.Zero, ref size, headerSize);
        if (result == uint.MaxValue || size == 0) return;

        IntPtr buffer = Marshal.AllocHGlobal((int)size);
        try
        {
            result = RawInputNativeMethods.GetRawInputData(
                rawInput, RawInputNativeMethods.RID_INPUT, buffer, ref size, headerSize);
            if (result == uint.MaxValue) return;

            var header = Marshal.PtrToStructure<RawInputNativeMethods.RAWINPUTHEADER>(buffer);
            if (header.dwType != RawInputNativeMethods.RIM_TYPEHID) return;

            int hidOffset = Marshal.SizeOf<RawInputNativeMethods.RAWINPUTHEADER>();
            uint reportSize = (uint)Marshal.ReadInt32(buffer, hidOffset);
            uint reportCount = (uint)Marshal.ReadInt32(buffer, hidOffset + 4);
            IntPtr data = IntPtr.Add(buffer, hidOffset + 8);

            string deviceName = GetDeviceName(header.hDevice);
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
                    ReportReceived?.Invoke(raw);
                }
                catch (Exception ex)
                {
                    Error?.Invoke(ex);
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
        RawInputNativeMethods.GetRawInputDeviceInfoW(
            device, RawInputNativeMethods.RIDI_DEVICENAME, IntPtr.Zero, ref size);
        if (size == 0) return string.Empty;

        var builder = new StringBuilder((int)size);
        RawInputNativeMethods.GetRawInputDeviceInfoW(
            device, RawInputNativeMethods.RIDI_DEVICENAME, builder, ref size);
        return builder.ToString();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
        _started.Dispose();
    }
}
