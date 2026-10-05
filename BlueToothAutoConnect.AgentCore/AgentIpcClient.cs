using System.IO.Pipes;
using System.Text.Json;
using BlueToothAutoConnect.PolicyEngine.IPC;

namespace BlueToothAutoConnect.AgentCore;

public class AgentIpcClient
{
    private const string PipeName = "BlueToothAutoConnect_PrivilegedHost";

    public async Task<IpcResponse?> SendAsync(IpcRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            await pipe.ConnectAsync(3000, cancellationToken);

            using var reader = new StreamReader(pipe, leaveOpen: true);
            await using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };

            await writer.WriteLineAsync(JsonSerializer.Serialize(request));
            var line = await reader.ReadLineAsync(cancellationToken);
            return line == null ? null : JsonSerializer.Deserialize<IpcResponse>(line);
        }
        catch (Exception ex)
        {
            return new IpcResponse { Status = IpcResponseStatus.Error, Message = ex.Message };
        }
    }

    public Task<IpcResponse?> ConnectDeviceAsync(string deviceId, CancellationToken cancellationToken = default) =>
        SendAsync(new IpcRequest
        {
            RequestType = IpcRequestType.ConnectToDevice,
            Parameters = new Dictionary<string, object> { ["deviceId"] = deviceId }
        }, cancellationToken);

    public Task<IpcResponse?> UnpairDeviceAsync(string deviceId, CancellationToken cancellationToken = default) =>
        SendAsync(new IpcRequest
        {
            RequestType = IpcRequestType.UnpairDevice,
            Parameters = new Dictionary<string, object> { ["deviceId"] = deviceId }
        }, cancellationToken);

    public Task<IpcResponse?> FinalizeConnectionAsync(
        string deviceId,
        bool connectionConfirmed = false,
        CancellationToken cancellationToken = default) =>
        SendAsync(new IpcRequest
        {
            RequestType = IpcRequestType.FinalizeConnection,
            Parameters = new Dictionary<string, object>
            {
                ["deviceId"] = deviceId,
                ["connectionConfirmed"] = connectionConfirmed
            }
        }, cancellationToken);

    public Task<IpcResponse?> PingAsync(CancellationToken cancellationToken = default) =>
        SendAsync(new IpcRequest { RequestType = IpcRequestType.Ping }, cancellationToken);
}
