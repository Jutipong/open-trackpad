namespace OpenTrackpad.Interop.Driver;

public readonly record struct ControlPanelResponse(byte Status, byte Value)
{
    public bool Ok => Status == ControlPanelProtocol.StatusOk;
}

public static class ControlPanelProtocol
{
    public const string DefaultPipeName = "AmtPtpControlPanelUsbUmInterface";

    public const byte CmdReloadSettings = 0x00;
    public const byte CmdGetBatteryLevel = 0x01;
    public const byte CmdActuate = 0x02;

    public const byte StatusOk = 0x00;
    public const byte StatusFail = 0xFF;
    public const byte StatusUnknown = 0xFE;

    public const uint MaxStrength = 0xFFFFFF;

    public static byte[] ReloadRequest() => new[] { CmdReloadSettings };

    public static byte[] BatteryRequest() => new[] { CmdGetBatteryLevel };

    public static byte[] ActuateRequest(uint strength)
    {
        if (strength > MaxStrength)
            throw new ArgumentOutOfRangeException(nameof(strength), "Haptic strength is 24-bit.");

        return new[]
        {
            CmdActuate,
            (byte)(strength & 0xFF),
            (byte)((strength >> 8) & 0xFF),
            (byte)((strength >> 16) & 0xFF),
        };
    }

    public static ControlPanelResponse ParseResponse(ReadOnlySpan<byte> response)
    {
        if (response.Length < 2)
            throw new InvalidDataException($"Control panel response must be 2 bytes, got {response.Length}.");
        return new ControlPanelResponse(response[0], response[1]);
    }
}
