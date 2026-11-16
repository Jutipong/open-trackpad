using System.IO.Pipes;

namespace OpenTrackpad.Interop.Driver;

public interface IControlPanelPipe
{
    ControlPanelResponse GetBattery(int timeoutMs = 3000);
    ControlPanelResponse ReloadSettings(int timeoutMs = 3000);
    ControlPanelResponse Actuate(uint strength, int timeoutMs = 3000);
}

/// <summary>
/// Client for the MagicTrackpad2ForWindows v2.3 control panel pipe.
/// The driver serves one request per connection, so every call opens a
/// fresh pipe connection.
/// </summary>
public sealed class NamedPipeControlPanelPipe : IControlPanelPipe
{
    public NamedPipeControlPanelPipe(string pipeName = ControlPanelProtocol.DefaultPipeName)
    {
        if (string.IsNullOrWhiteSpace(pipeName))
            throw new ArgumentException("Pipe name is required.", nameof(pipeName));
        PipeName = pipeName;
    }

    public string PipeName { get; }

    public ControlPanelResponse GetBattery(int timeoutMs = 3000) =>
        Send(ControlPanelProtocol.BatteryRequest(), timeoutMs);

    public ControlPanelResponse ReloadSettings(int timeoutMs = 3000) =>
        Send(ControlPanelProtocol.ReloadRequest(), timeoutMs);

    public ControlPanelResponse Actuate(uint strength, int timeoutMs = 3000) =>
        Send(ControlPanelProtocol.ActuateRequest(strength), timeoutMs);

    private ControlPanelResponse Send(byte[] request, int timeoutMs)
    {
        if (timeoutMs <= 0)
            throw new ArgumentOutOfRangeException(nameof(timeoutMs));

        using var pipe = new NamedPipeClientStream(
            ".", PipeName, PipeDirection.InOut, PipeOptions.None,
            System.Security.Principal.TokenImpersonationLevel.Impersonation);

        pipe.Connect(timeoutMs);
        pipe.ReadMode = PipeTransmissionMode.Message;
        pipe.Write(request);
        pipe.Flush();

        var response = new byte[2];
        int read = 0;
        while (read < response.Length)
        {
            int n = pipe.Read(response, read, response.Length - read);
            if (n == 0)
                throw new IOException("Control panel pipe closed before a full response.");
            read += n;
        }

        return ControlPanelProtocol.ParseResponse(response);
    }
}
