using System.Collections.ObjectModel;
using System.Collections.Concurrent;
using BlueToothAutoConnect.AgentCore;
using BlueToothAutoConnect.Persistence.Repositories;
using BlueToothAutoConnect.PolicyEngine.Models;
using BlueToothAutoConnect.PolicyEngine.Policy;
using Microsoft.UI.Dispatching;
using Windows.Devices.Enumeration;

namespace BlueToothAutoConnect_UserAgent.Services;

public sealed class BluetoothAgentViewModelAdapter : IDisposable
{
    private readonly BluetoothAgentWatcher _agent;
    private readonly DispatcherQueue? _dispatcherQueue;
    private readonly IWhitelistRepository _whitelistRepository;
    private readonly ConcurrentDictionary<string, byte> _discoveredDevices = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, byte> _whitelistedDevices = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, byte> _connectedWhitelistedDevices = new(StringComparer.Ordinal);

    public ObservableCollection<DeviceInfo> Devices { get; } = [];
    public event Action? DevicesChanged;
    public event Action<string>? WhitelistedDeviceConnected;
    public event Action<string>? WhitelistedDeviceDisconnected;
    public event Action? DiscoveryEnumerationCompleted;
    public event Action<string>? DiscoveryFailed;
    public event Action<string>? LogMessage;

    public BluetoothAgentViewModelAdapter(
        IPolicyEngine policyEngine,
        AgentIpcClient ipcClient,
        IWhitelistRepository whitelistRepository)
    {
        _whitelistRepository = whitelistRepository;
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
        var pairingPrompt = new WinUiPairingPrompt(_dispatcherQueue ?? throw new InvalidOperationException("UI dispatcher is unavailable"));
        var userSessionBluetooth = new UserSessionBluetoothService(pairingPrompt.Handle);
        _agent = new BluetoothAgentWatcher(ipcClient, userSessionBluetooth, policyEngine, autoConnect: true);
        _agent.DeviceDiscovered += OnDeviceDiscovered;
        _agent.DeviceUpdated += OnDeviceUpdated;
        _agent.DeviceRemoved += OnDeviceRemoved;
        _agent.DiscoveryEnumerationCompleted += () => Dispatch(() => DiscoveryEnumerationCompleted?.Invoke());
        _agent.DiscoveryFailed += message => Dispatch(() => DiscoveryFailed?.Invoke(message));
        _agent.LogMessage += message => Dispatch(() => LogMessage?.Invoke(message));
    }

    public void Start() => _agent.Start();

    public async Task RefreshDiscoveryAsync()
    {
        Devices.Clear();
        DevicesChanged?.Invoke();
        await _agent.RefreshDiscoveryAsync();
    }

    public void Stop() => _agent.Stop();

    public Task<(bool Success, string Message)> ConnectAsync(string deviceId) => _agent.ConnectAsync(deviceId);

    public Task<(bool Success, string Message)> UnpairAsync(string deviceId) => _agent.UnpairAsync(deviceId);

    public Task<BlueToothAutoConnect.PolicyEngine.IPC.IpcResponse?> PingAsync() => _agent.PingAsync();

    private void OnDeviceDiscovered(DeviceInfo device)
    {
        _discoveredDevices.TryAdd(device.Id, 0);
        _ = TrackWhitelistMatchAsync(device);
        Dispatch(() =>
        {
            var existing = Devices.FirstOrDefault(item => item.Id == device.Id);
            if (existing == null)
            {
                Devices.Add(device);
                var sameNameCount = Devices.Count(item =>
                    string.Equals(item.Name, device.Name, StringComparison.OrdinalIgnoreCase));
                DiagnosticLog.Write(
                    $"DEVICE discovered name=\"{device.Name}\" address=\"{device.Address}\" id=\"{device.Id}\" sameNameCount={sameNameCount} paired={device.IsPaired} connectable={device.IsConnectable}");
                if (sameNameCount > 1)
                    DiagnosticLog.Write($"DEVICE duplicate-name detected name=\"{device.Name}\" count={sameNameCount}");
            }
            else
                CopyDeviceState(existing, device);
            DevicesChanged?.Invoke();
        });
    }

    private void OnDeviceUpdated(DeviceInfo device) => Dispatch(() =>
    {
        var existing = Devices.FirstOrDefault(item => item.Id == device.Id);
        if (existing == null)
        {
            Devices.Add(device);
        }
        else
        {
            var wasConnected = existing.IsConnected;
            CopyDeviceState(existing, device);
            if (_whitelistedDevices.ContainsKey(device.Id))
            {
                if (!wasConnected && device.IsConnected &&
                    _connectedWhitelistedDevices.TryAdd(device.Id, 0))
                {
                    WhitelistedDeviceConnected?.Invoke(GetDisplayName(device));
                }
                else if (wasConnected && !device.IsConnected &&
                         _connectedWhitelistedDevices.TryRemove(device.Id, out _))
                {
                    WhitelistedDeviceDisconnected?.Invoke(GetDisplayName(device));
                }
            }
        }
        DevicesChanged?.Invoke();
    });

    private void OnDeviceRemoved(string deviceId) => Dispatch(() =>
    {
        _discoveredDevices.TryRemove(deviceId, out _);
        var existing = Devices.FirstOrDefault(item => item.Id == deviceId);
        if (existing != null)
        {
            if (_connectedWhitelistedDevices.TryRemove(deviceId, out _))
                WhitelistedDeviceDisconnected?.Invoke(GetDisplayName(existing));
            Devices.Remove(existing);
            DiagnosticLog.Write($"DEVICE removed name=\"{existing.Name}\" address=\"{existing.Address}\" id=\"{existing.Id}\"");
        }
        else
        {
            _connectedWhitelistedDevices.TryRemove(deviceId, out _);
        }
        _whitelistedDevices.TryRemove(deviceId, out _);
        DevicesChanged?.Invoke();
    });

    private void Dispatch(Action action)
    {
        if (_dispatcherQueue != null && !_dispatcherQueue.HasThreadAccess)
        {
            if (!_dispatcherQueue.TryEnqueue(() => InvokeSafely(action)))
                DiagnosticLog.Write("UI dispatcher rejected a Bluetooth device update.");
            return;
        }

        InvokeSafely(action);
    }

    private static void InvokeSafely(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            DiagnosticLog.WriteException("Bluetooth device UI update callback", ex);
        }
    }

    private static void CopyDeviceState(DeviceInfo target, DeviceInfo source)
    {
        target.Name = source.Name;
        target.Address = source.Address;
        target.Category = source.Category;
        target.IsConnected = source.IsConnected;
        target.IsConnectable = source.IsConnectable;
        target.IsPaired = source.IsPaired;
        target.LastSeen = source.LastSeen;
    }

    private async Task TrackWhitelistMatchAsync(DeviceInfo device)
    {
        try
        {
            var entry = await _whitelistRepository.FindMatchingEntryAsync(
                device.Address, device.Name, device.Category);
            if (entry?.IsActive == true && _discoveredDevices.ContainsKey(device.Id))
                _whitelistedDevices.TryAdd(device.Id, 0);
        }
        catch (Exception ex)
        {
            DiagnosticLog.WriteException("Bluetooth whitelist notification tracking", ex);
        }
    }

    private static string GetDisplayName(DeviceInfo device) =>
        string.IsNullOrWhiteSpace(device.Name) ? "Unknown Bluetooth device" : device.Name;

    public void Dispose() => _agent.Dispose();
}
