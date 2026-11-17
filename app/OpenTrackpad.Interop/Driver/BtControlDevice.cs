using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace OpenTrackpad.Interop.Driver;

/// <summary>
/// Bluetooth control channel exposed by MagicTrackpad2ForWindows v2.3.
/// The control device is administrator-only; the haptic device accepts
/// IOCTL_PTPFILTER_ACTUATE from interactive users when AllowNonAdminAccess=1.
/// </summary>
public sealed class BtControlDevice : IControlPanelPipe
{
    public const string ControlDevicePath = @"\\.\AmtPtpControlDeviceUm";
    public const string HapticDevicePath = @"\\.\AmtPtpHapticDeviceUm";

    private const uint GenericRead = 0x80000000;
    private const uint GenericWrite = 0x40000000;
    private const uint FileShareRead = 0x00000001;
    private const uint FileShareWrite = 0x00000002;
    private const uint OpenExisting = 3;

    public ControlPanelResponse GetBattery(int timeoutMs = 3000)
    {
        using SafeFileHandle device = Open(ControlDevicePath, GenericRead | GenericWrite);
        var output = new byte[4];
        Ioctl(device, BtIoctl.GetBattery, input: null, output);
        int level = BitConverter.ToInt32(output, 0);
        return level is >= 0 and <= 100
            ? new ControlPanelResponse(ControlPanelProtocol.StatusOk, (byte)level)
            : new ControlPanelResponse(ControlPanelProtocol.StatusFail, 0);
    }

    public ControlPanelResponse ReloadSettings(int timeoutMs = 3000)
    {
        using SafeFileHandle device = Open(ControlDevicePath, GenericRead | GenericWrite);
        Ioctl(device, BtIoctl.ReloadSettings, input: null, output: null);
        return new ControlPanelResponse(ControlPanelProtocol.StatusOk, 0);
    }

    public ControlPanelResponse Actuate(uint strength, int timeoutMs = 3000)
    {
        if (strength > ControlPanelProtocol.MaxStrength)
            throw new ArgumentOutOfRangeException(nameof(strength), "Haptic strength is 24-bit.");

        byte[] input = BitConverter.GetBytes(strength);
        try
        {
            using SafeFileHandle device = Open(HapticDevicePath, GenericWrite);
            Ioctl(device, BtIoctl.Actuate, input, output: null);
        }
        catch (Exception hapticError)
        {
            // The control device also accepts ACTUATE, but only for administrators.
            try
            {
                using SafeFileHandle device = Open(ControlDevicePath, GenericRead | GenericWrite);
                Ioctl(device, BtIoctl.Actuate, input, output: null);
            }
            catch
            {
                throw hapticError;
            }
        }

        return new ControlPanelResponse(ControlPanelProtocol.StatusOk, 0);
    }

    private static SafeFileHandle Open(string path, uint access)
    {
        SafeFileHandle handle = NativeMethods.CreateFileW(
            path, access, FileShareRead | FileShareWrite,
            IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);

        if (handle.IsInvalid)
        {
            int error = Marshal.GetLastWin32Error();
            handle.Dispose();
            throw new Win32Exception(error, $"Open {path} failed");
        }

        return handle;
    }

    private static void Ioctl(
        SafeFileHandle device, uint code, byte[]? input, byte[]? output)
    {
        uint inputSize = input is null ? 0 : (uint)input.Length;
        uint outputSize = output is null ? 0 : (uint)output.Length;

        if (!NativeMethods.DeviceIoControl(
                device, code, input, inputSize, output, outputSize,
                out _, IntPtr.Zero))
        {
            int error = Marshal.GetLastWin32Error();
            throw new Win32Exception(error, $"DeviceIoControl 0x{code:X8} failed");
        }
    }

    private static class NativeMethods
    {
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern SafeFileHandle CreateFileW(
            string fileName,
            uint desiredAccess,
            uint shareMode,
            IntPtr securityAttributes,
            uint creationDisposition,
            uint flagsAndAttributes,
            IntPtr templateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool DeviceIoControl(
            SafeFileHandle device,
            uint ioControlCode,
            [In] byte[]? inBuffer,
            uint inBufferSize,
            [Out] byte[]? outBuffer,
            uint outBufferSize,
            out uint bytesReturned,
            IntPtr overlapped);
    }
}

public static class BtIoctl
{
    private const uint FileDeviceUnknown = 0x00000022;
    private const uint MethodBuffered = 0;
    private const uint FileAnyAccess = 0;
    private const uint FileWriteAccess = 2;

    private static uint CtlCode(uint function, uint access) =>
        (FileDeviceUnknown << 16) | (access << 14) | (function << 2) | MethodBuffered;

    public static readonly uint ReloadSettings = CtlCode(0x800, FileAnyAccess);
    public static readonly uint GetBattery = CtlCode(0x801, FileAnyAccess);
    public static readonly uint Actuate = CtlCode(0x802, FileWriteAccess);
}
