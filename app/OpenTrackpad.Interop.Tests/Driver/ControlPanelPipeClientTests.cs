using System.IO.Pipes;
using OpenTrackpad.Interop.Driver;

namespace OpenTrackpad.Interop.Tests.Driver;

public sealed class ControlPanelPipeClientTests
{
    [Fact]
    public async Task GetBattery_SendsCommandAndReadsResponse()
    {
        string pipeName = NewPipeName();
        var server = RunFakeServer(pipeName, request =>
        {
            Assert.Equal(new byte[] { 0x01 }, request);
            return new byte[] { 0x00, 87 };
        });

        var client = new NamedPipeControlPanelPipe(pipeName);
        ControlPanelResponse response = await Task.Run(() => client.GetBattery());

        await server;
        Assert.True(response.Ok);
        Assert.Equal(87, response.Value);
    }

    [Fact]
    public async Task Actuate_SendsStrengthBytes()
    {
        string pipeName = NewPipeName();
        var server = RunFakeServer(pipeName, request =>
        {
            Assert.Equal(new byte[] { 0x02, 0x17, 0x06, 0x06 }, request);
            return new byte[] { 0x00, 0x00 };
        });

        var client = new NamedPipeControlPanelPipe(pipeName);
        ControlPanelResponse response = await Task.Run(() => client.Actuate(0x060617));

        await server;
        Assert.True(response.Ok);
    }

    [Fact]
    public async Task Reload_SendsCommand()
    {
        string pipeName = NewPipeName();
        var server = RunFakeServer(pipeName, request =>
        {
            Assert.Equal(new byte[] { 0x00 }, request);
            return new byte[] { 0x00, 0x00 };
        });

        var client = new NamedPipeControlPanelPipe(pipeName);
        ControlPanelResponse response = await Task.Run(() => client.ReloadSettings());

        await server;
        Assert.True(response.Ok);
    }

    private static string NewPipeName() => "OpenTrackpadTest_" + Guid.NewGuid().ToString("N");

    private static Task RunFakeServer(string pipeName, Func<byte[], byte[]> handler) =>
        Task.Run(async () =>
        {
            using var server = new NamedPipeServerStream(
                pipeName,
                PipeDirection.InOut,
                maxNumberOfServerInstances: 1,
                PipeTransmissionMode.Message,
                PipeOptions.Asynchronous);

            await server.WaitForConnectionAsync();

            var request = new byte[64];
            int read = await server.ReadAsync(request);
            byte[] actual = request.AsSpan(0, read).ToArray();

            byte[] response = handler(actual);
            await server.WriteAsync(response);
            await server.FlushAsync();
        });
}
