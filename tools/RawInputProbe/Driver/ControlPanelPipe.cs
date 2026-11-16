using System.IO.Pipes;

namespace RawInputProbe.Driver;

public sealed record PipeResponse(byte Status, byte Value)
{
    public bool Ok => Status == 0x00;
}

public static class ControlPanelPipe
{
    public const string DefaultPipeName = "AmtPtpControlPanelUsbUmInterface";
    public const byte CmdReloadSettings = 0x00;
    public const byte CmdGetBatteryLevel = 0x01;
    public const byte CmdActuate = 0x02;

    public static PipeResponse Send(ReadOnlySpan<byte> request, int timeoutMs = 3000, string pipeName = DefaultPipeName)
    {
        using var pipe = new NamedPipeClientStream(
            ".", pipeName, PipeDirection.InOut, PipeOptions.None,
            System.Security.Principal.TokenImpersonationLevel.Impersonation);

        pipe.Connect(timeoutMs);
        pipe.ReadMode = PipeTransmissionMode.Message;
        pipe.Write(request);
        pipe.Flush();

        var response = new byte[2];
        int read = pipe.Read(response, 0, response.Length);
        if (read < 2)
            throw new IOException($"Short pipe response ({read} bytes).");
        return new PipeResponse(response[0], response[1]);
    }

    public static PipeResponse GetBattery(int timeoutMs = 3000) =>
        Send(new byte[] { CmdGetBatteryLevel }, timeoutMs);

    public static PipeResponse ReloadSettings(int timeoutMs = 3000) =>
        Send(new byte[] { CmdReloadSettings }, timeoutMs);

    public static PipeResponse Actuate(uint strength, int timeoutMs = 3000) =>
        Send(new byte[]
        {
            CmdActuate,
            (byte)(strength & 0xFF),
            (byte)((strength >> 8) & 0xFF),
            (byte)((strength >> 16) & 0xFF),
        }, timeoutMs);

    public static uint ParseStrength(string? text) => text?.ToLowerInvariant() switch
    {
        null or "" or "medium" => 0x060617,
        "light" => 0x040415,
        "firm" => 0x08081E,
        "max" or "maximum" => 0x0A0A20,
        _ => ParseHex24(text),
    };

    private static uint ParseHex24(string text)
    {
        string value = text.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? text[2..] : text;
        if (!uint.TryParse(value, System.Globalization.NumberStyles.HexNumber, null, out uint result) ||
            result > 0xFFFFFF)
        {
            throw new FormatException($"Invalid haptic strength: {text}");
        }
        return result;
    }
}
