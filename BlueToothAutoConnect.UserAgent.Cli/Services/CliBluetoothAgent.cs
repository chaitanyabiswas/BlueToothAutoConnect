using BlueToothAutoConnect.AgentCore;
using BlueToothAutoConnect.PolicyEngine.Models;
using BlueToothAutoConnect.PolicyEngine.Policy;
using Windows.Devices.Enumeration;

namespace BlueToothAutoConnect_UserAgent_Cli.Services;

internal sealed class CliBluetoothAgent : IDisposable
{
    private readonly BluetoothAgentWatcher _watcher;

    public event Action<DeviceInfo>? DeviceDiscovered
    {
        add => _watcher.DeviceDiscovered += value;
        remove => _watcher.DeviceDiscovered -= value;
    }

    public event Action<DeviceInfo>? DeviceUpdated
    {
        add => _watcher.DeviceUpdated += value;
        remove => _watcher.DeviceUpdated -= value;
    }

    public event Action<string>? DeviceRemoved
    {
        add => _watcher.DeviceRemoved += value;
        remove => _watcher.DeviceRemoved -= value;
    }

    public event Action<string>? LogMessage
    {
        add => _watcher.LogMessage += value;
        remove => _watcher.LogMessage -= value;
    }

    public IReadOnlyCollection<DeviceInfo> DiscoveredDevices => _watcher.DiscoveredDevices;

    public CliBluetoothAgent(
        IPolicyEngine? policyEngine = null,
        AgentIpcClient? ipcClient = null,
        bool autoConnect = false)
    {
        var pairing = new UserSessionBluetoothService(ConsolePairingPrompt.Handle);
        _watcher = new BluetoothAgentWatcher(
            ipcClient ?? new AgentIpcClient(), pairing, policyEngine, autoConnect);
    }

    public void Start() => _watcher.Start();

    public void Stop() => _watcher.Stop();

    public Task<(bool Success, string Message)> ConnectAsync(string deviceId) => _watcher.ConnectAsync(deviceId);

    public Task<(bool Success, string Message)> UnpairAsync(string deviceId) => _watcher.UnpairAsync(deviceId);

    public Task<BlueToothAutoConnect.PolicyEngine.IPC.IpcResponse?> PingAsync() => _watcher.PingAsync();

    public void Dispose() => _watcher.Dispose();
}
