using System.Collections.Concurrent;
using BlueToothAutoConnect.PolicyEngine.IPC;
using BlueToothAutoConnect.PolicyEngine.Models;
using BlueToothAutoConnect.PolicyEngine.Policy;
using Windows.Devices.Enumeration;

namespace BlueToothAutoConnect.AgentCore;

public sealed class BluetoothAgentWatcher : IDisposable
{
    private const string AqsFilter =
        "System.Devices.Aep.ProtocolId:=\"{e0cbf06c-cd8b-4647-bb8a-263b43f0f974}\" OR " +
        "System.Devices.Aep.ProtocolId:=\"{bb7bb05e-5972-42b5-94fc-76eaa7084d49}\"";

    private static readonly string[] RequestedProperties =
    [
        "System.Devices.Aep.DeviceAddress",
        "System.Devices.Aep.IsConnected",
        "System.Devices.Aep.Bluetooth.Le.IsConnectable",
        "System.Devices.Aep.IsPaired"
    ];

    private readonly IPolicyEngine? _policyEngine;
    private readonly AgentIpcClient _ipcClient;
    private readonly UserSessionBluetoothService _userSessionBluetooth;
    private readonly bool _autoConnect;
    private readonly ConcurrentDictionary<string, DeviceInfo> _devices = new();
    private readonly ConcurrentDictionary<string, byte> _inFlightConnections = new();
    private readonly ConcurrentDictionary<string, DateTime> _successfulConnectionCooldowns = new();
    private DeviceWatcher? _watcher;
    private bool _stopRequested;

    public event Action<DeviceInfo>? DeviceDiscovered;
    public event Action<DeviceInfo>? DeviceUpdated;
    public event Action<string>? DeviceRemoved;
    public event Action<string>? LogMessage;
    public event Action? DiscoveryEnumerationCompleted;
    public event Action<string>? DiscoveryFailed;

    public IReadOnlyCollection<DeviceInfo> DiscoveredDevices => _devices.Values.ToList();

    public BluetoothAgentWatcher(
        AgentIpcClient ipcClient,
        UserSessionBluetoothService userSessionBluetooth,
        IPolicyEngine? policyEngine = null,
        bool autoConnect = false)
    {
        _ipcClient = ipcClient;
        _userSessionBluetooth = userSessionBluetooth;
        _policyEngine = policyEngine;
        _autoConnect = autoConnect;
    }

    public void Start()
    {
        if (_watcher?.Status is DeviceWatcherStatus.Started or DeviceWatcherStatus.EnumerationCompleted)
            return;

        var watcher = DeviceInformation.CreateWatcher(AqsFilter, RequestedProperties,
            DeviceInformationKind.AssociationEndpoint);
        watcher.Added += OnAdded;
        watcher.Updated += OnUpdated;
        watcher.Removed += OnRemoved;
        watcher.EnumerationCompleted += (_, _) => DiscoveryEnumerationCompleted?.Invoke();
        watcher.Stopped += (_, _) =>
        {
            if (!_stopRequested)
                DiscoveryFailed?.Invoke("Bluetooth device discovery stopped unexpectedly.");
        };
        _watcher = watcher;
        _stopRequested = false;

        try
        {
            watcher.Start();
        }
        catch
        {
            _watcher = null;
            throw;
        }
    }

    public void Stop()
    {
        if (_watcher?.Status is DeviceWatcherStatus.Started or DeviceWatcherStatus.EnumerationCompleted)
        {
            _stopRequested = true;
            _watcher.Stop();
        }
    }

    public async Task RefreshDiscoveryAsync()
    {
        var currentWatcher = _watcher;
        if (currentWatcher?.Status is DeviceWatcherStatus.Started or DeviceWatcherStatus.EnumerationCompleted)
        {
            var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            currentWatcher.Stopped += (_, _) => stopped.TrySetResult();
            _stopRequested = true;
            currentWatcher.Stop();
            await stopped.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }

        _watcher = null;
        _devices.Clear();
        Start();
    }

    public async Task<(bool Success, string Message)> ConnectAsync(string deviceId, CancellationToken cancellationToken = default)
    {
        var response = await _ipcClient.ConnectDeviceAsync(deviceId, cancellationToken);
        var success = response?.Status == IpcResponseStatus.Success;
        var message = response?.Message ?? "Privileged Host did not return a response";

        if (!success)
        {
            LogMessage?.Invoke($"Privileged Host connection failed: {message}");
            LogMessage?.Invoke("Retrying from the signed-in user session...");
            var userResult = await _userSessionBluetooth.ConnectAsync(deviceId);
            success = userResult.Success;
            message = userResult.Message;

            if (success)
            {
                var finalizeResponse = await _ipcClient.FinalizeConnectionAsync(
                    deviceId, connectionConfirmed: true, cancellationToken);
                message = await AppendPendingUnpairResultsAsync(message, finalizeResponse);
            }
        }
        else
        {
            message = await AppendPendingUnpairResultsAsync(message, response);
        }

        return (success, message);
    }

    public async Task<(bool Success, string Message)> UnpairAsync(string deviceId, CancellationToken cancellationToken = default)
    {
        var response = await _ipcClient.UnpairDeviceAsync(deviceId, cancellationToken);
        if (response?.Status == IpcResponseStatus.Success)
            return (true, response.Message);

        LogMessage?.Invoke($"Privileged Host unpair failed: {response?.Message}");
        LogMessage?.Invoke("Retrying unpair from the signed-in user session...");
        return await _userSessionBluetooth.UnpairAsync(deviceId);
    }

    public Task<IpcResponse?> PingAsync(CancellationToken cancellationToken = default) =>
        _ipcClient.PingAsync(cancellationToken);

    private async Task<string> AppendPendingUnpairResultsAsync(string message, IpcResponse? response)
    {
        if (response == null)
            return message;

        foreach (var deviceId in response.PendingUnpairDeviceIds)
        {
            var result = await _userSessionBluetooth.UnpairAsync(deviceId);
            LogMessage?.Invoke($"{(result.Success ? "Removed" : "Could not remove")} {deviceId}: {result.Message}");
        }

        return message;
    }

    private void OnAdded(DeviceWatcher sender, DeviceInformation info)
    {
        try
        {
            var device = ToDeviceInfo(info);
            _devices[device.Id] = device;
            DeviceDiscovered?.Invoke(device);
            if (_autoConnect)
                _ = EvaluateAndConnectAsync(device);
        }
        catch (Exception ex)
        {
            LogMessage?.Invoke($"Bluetooth device-added callback failed: {ex}");
        }
    }

    private void OnUpdated(DeviceWatcher sender, DeviceInformationUpdate update)
    {
        try
        {
            if (!_devices.TryGetValue(update.Id, out var existing))
                return;

            ApplyUpdate(existing, update);
            if (existing.IsConnected)
                _successfulConnectionCooldowns.TryRemove(existing.Id, out _);
            DeviceUpdated?.Invoke(existing);
            if (_autoConnect)
                _ = EvaluateAndConnectAsync(existing);
        }
        catch (Exception ex)
        {
            LogMessage?.Invoke($"Bluetooth device-updated callback failed for id '{update.Id}': {ex}");
        }
    }

    private void OnRemoved(DeviceWatcher sender, DeviceInformationUpdate update)
    {
        try
        {
            _successfulConnectionCooldowns.TryRemove(update.Id, out _);
            if (_devices.TryRemove(update.Id, out _))
                DeviceRemoved?.Invoke(update.Id);
        }
        catch (Exception ex)
        {
            LogMessage?.Invoke($"Bluetooth device-removed callback failed for id '{update.Id}': {ex}");
        }
    }

    private async Task EvaluateAndConnectAsync(DeviceInfo device)
    {
        if (_policyEngine == null)
            return;

        if (_successfulConnectionCooldowns.TryGetValue(device.Id, out var retryAfterUtc))
        {
            if (device.IsConnected || DateTime.UtcNow >= retryAfterUtc)
                _successfulConnectionCooldowns.TryRemove(device.Id, out _);
            else
                return;
        }

        var lockKey = !string.IsNullOrEmpty(device.Address) ? device.Address :
            !string.IsNullOrEmpty(device.Name) ? device.Name : device.Id;
        if (!_inFlightConnections.TryAdd(lockKey, 0))
            return;

        try
        {
            var decision = await _policyEngine.EvaluateConnectionPolicyAsync(device);
            if (decision.ShouldConnect)
            {
                LogMessage?.Invoke($"Policy triggered auto-connect for {device.Name} ({device.Address})");
                var (success, message) = await ConnectAsync(device.Id);
                LogMessage?.Invoke($"Connection response for {device.Name}: {(success ? "Success" : "Error")} - {message}");
                if (success)
                    _successfulConnectionCooldowns[device.Id] = DateTime.UtcNow.AddSeconds(30);

                var retryPolicy = _policyEngine.GetRetryPolicy(lockKey);
                var delay = retryPolicy.RecordAttempt(success);
                if (!success)
                    LogMessage?.Invoke($"Backing off connection for {device.Name} for {(int)delay.TotalSeconds}s (attempt #{retryPolicy.CurrentAttemptCount})");
            }
            else if (decision.IsWhitelisted && device.IsConnectable && !device.IsConnected &&
                     decision.Reason?.Contains("backoff", StringComparison.OrdinalIgnoreCase) == true)
            {
                LogMessage?.Invoke($"Waiting to retry {device.Name} ({device.Address}): {decision.Reason}");
            }
        }
        catch (Exception ex)
        {
            LogMessage?.Invoke($"Error evaluating policy for {device.Name}: {ex}");
        }
        finally
        {
            _inFlightConnections.TryRemove(lockKey, out _);
        }
    }

    private static DeviceInfo ToDeviceInfo(DeviceInformation info)
    {
        info.Properties.TryGetValue("System.Devices.Aep.DeviceAddress", out var address);
        info.Properties.TryGetValue("System.Devices.Aep.IsConnected", out var connected);
        info.Properties.TryGetValue("System.Devices.Aep.Bluetooth.Le.IsConnectable", out var connectable);
        info.Properties.TryGetValue("System.Devices.Aep.IsPaired", out var paired);
        var name = info.Name ?? string.Empty;

        return new DeviceInfo
        {
            Id = info.Id,
            Name = name,
            Address = address?.ToString() ?? string.Empty,
            Category = DeviceCategoryClassifier.Infer(name),
            IsConnected = connected is true,
            IsConnectable = connectable is true,
            IsPaired = paired is true,
            LastSeen = DateTime.UtcNow
        };
    }

    private static void ApplyUpdate(DeviceInfo target, DeviceInformationUpdate update)
    {
        if (update.Properties.TryGetValue("System.Devices.Aep.IsConnected", out var connected)) target.IsConnected = connected is true;
        if (update.Properties.TryGetValue("System.Devices.Aep.Bluetooth.Le.IsConnectable", out var connectable)) target.IsConnectable = connectable is true;
        if (update.Properties.TryGetValue("System.Devices.Aep.IsPaired", out var paired)) target.IsPaired = paired is true;
        if (update.Properties.TryGetValue("System.Devices.Aep.DeviceAddress", out var address)) target.Address = address?.ToString() ?? target.Address;
        target.LastSeen = DateTime.UtcNow;
    }

    public void Dispose() => Stop();
}
