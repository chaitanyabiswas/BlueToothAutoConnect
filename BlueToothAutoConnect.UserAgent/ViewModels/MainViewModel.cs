using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using BlueToothAutoConnect.Persistence.Entities;
using BlueToothAutoConnect.Persistence.Repositories;
using BlueToothAutoConnect.AgentCore;
using BlueToothAutoConnect.PolicyEngine.Models;
using BlueToothAutoConnect_UserAgent.Services;

namespace BlueToothAutoConnect_UserAgent.ViewModels;

public class MainViewModel : INotifyPropertyChanged
{
    private readonly IWhitelistRepository? _whitelist;
    private readonly AgentIpcClient? _ipcClient;
    private readonly BluetoothAgentViewModelAdapter? _agent;
    private bool _isWhitelistLoading;
    private bool _isDeviceDiscoveryLoading;
    private bool _deviceDiscoveryFailed;
    private bool _deviceDiscoveryStarted;
    private string _whitelistStatus = "Waiting to load trusted devices...";
    private string _deviceDiscoveryStatus = "Waiting to scan for Bluetooth devices...";

    public ObservableCollection<DeviceInfo> Devices { get; } = [];
    public ObservableCollection<WhitelistEntry> WhitelistEntries { get; } = [];
    public ObservableCollection<string> Logs { get; } = [];

    public bool IsWhitelistLoading
    {
        get => _isWhitelistLoading;
        private set { _isWhitelistLoading = value; OnPropertyChanged(); }
    }

    public bool IsDeviceDiscoveryLoading
    {
        get => _isDeviceDiscoveryLoading;
        private set { _isDeviceDiscoveryLoading = value; OnPropertyChanged(); }
    }

    public string WhitelistStatus
    {
        get => _whitelistStatus;
        private set { _whitelistStatus = value; OnPropertyChanged(); }
    }

    public string DeviceDiscoveryStatus
    {
        get => _deviceDiscoveryStatus;
        private set { _deviceDiscoveryStatus = value; OnPropertyChanged(); }
    }

    private string _serviceStatus = "Unknown";
    public string ServiceStatus
    {
        get => _serviceStatus;
        set { _serviceStatus = value; OnPropertyChanged(); }
    }

    public MainViewModel(BluetoothAgentViewModelAdapter? watcher, IWhitelistRepository? whitelist, AgentIpcClient? ipcClient)
    {
        _whitelist = whitelist;
        _ipcClient = ipcClient;
        _agent = watcher;
        if (watcher != null)
        {
            Devices = watcher.Devices;
            watcher.DevicesChanged += OnDevicesChanged;
            watcher.DiscoveryEnumerationCompleted += OnDiscoveryEnumerationCompleted;
            watcher.DiscoveryFailed += OnDiscoveryFailed;
            watcher.LogMessage += Log;
        }
    }

    public async Task InitializeAsync()
    {
        await Task.Yield();
        StartDeviceDiscovery();
        _ = CheckServiceStatusAsync();
        await RefreshWhitelistAsync(App.InitializeDatabaseAsync);
    }

    public async Task RefreshWhitelistAsync(Func<Task>? initializeDatabaseAsync = null)
    {
        if (IsWhitelistLoading)
            return;

        IsWhitelistLoading = true;
        WhitelistStatus = "Loading trusted devices...";
        try
        {
            await (initializeDatabaseAsync?.Invoke() ?? Task.CompletedTask);
            if (_whitelist == null)
                throw new InvalidOperationException("Whitelist storage is unavailable.");

            var entries = await _whitelist.GetAllActiveAsync();
            WhitelistEntries.Clear();
            foreach (var entry in entries)
                WhitelistEntries.Add(entry);

            WhitelistStatus = entries.Count == 0
                ? "No trusted devices yet."
                : string.Empty;
        }
        catch (Exception ex)
        {
            WhitelistStatus = "Could not load trusted devices. Select Refresh to try again.";
            Log($"Whitelist refresh failed: {ex.Message}");
        }
        finally
        {
            IsWhitelistLoading = false;
        }
    }

    public void StartDeviceDiscovery()
    {
        if (IsDeviceDiscoveryLoading || _deviceDiscoveryStarted)
            return;

        if (_agent == null)
        {
            IsDeviceDiscoveryLoading = false;
            _deviceDiscoveryFailed = true;
            DeviceDiscoveryStatus = "Bluetooth discovery is unavailable. Select Refresh to try again.";
            return;
        }

        IsDeviceDiscoveryLoading = true;
        _deviceDiscoveryFailed = false;
        DeviceDiscoveryStatus = "Scanning for Bluetooth devices...";
        try
        {
            _agent.Start();
            _deviceDiscoveryStarted = true;
        }
        catch (Exception ex)
        {
            OnDiscoveryFailed($"Could not start Bluetooth discovery: {ex.Message}");
        }
    }

    public async Task RefreshDeviceDiscoveryAsync()
    {
        if (IsDeviceDiscoveryLoading)
            return;

        if (_agent == null)
        {
            OnDiscoveryFailed("Bluetooth discovery is unavailable.");
            return;
        }

        IsDeviceDiscoveryLoading = true;
        _deviceDiscoveryFailed = false;
        _deviceDiscoveryStarted = false;
        DeviceDiscoveryStatus = "Refreshing Bluetooth devices...";
        try
        {
            await _agent.RefreshDiscoveryAsync();
            _deviceDiscoveryStarted = true;
        }
        catch (Exception ex)
        {
            OnDiscoveryFailed($"Could not refresh Bluetooth discovery: {ex.Message}");
        }
    }

    public async Task AddToWhitelistAsync(
        DeviceInfo device,
        bool matchAddress = true,
        bool matchName = true,
        bool matchCategory = true)
    {
        if (_whitelist == null)
        {
            Log("Whitelist storage is unavailable");
            return;
        }

        if (!matchAddress && !matchName && !matchCategory)
        {
            Log("Select at least one trust matching criterion");
            return;
        }

        try
        {
            if (matchAddress && await _whitelist.ExistsByDeviceAddressAsync(device.Address))
            {
                Log($"Device {device.Name} already whitelisted");
                return;
            }
            await _whitelist.AddAsync(new WhitelistEntry
            {
                DeviceAddress = matchAddress ? device.Address : string.Empty,
                DeviceName = matchName ? device.Name : null,
                Category = matchCategory ? device.Category : null,
                FriendlyName = device.Name,
                AddedByUser = true,
                AddedAt = DateTime.UtcNow
            });
            var selectedCriteria = new[]
            {
                matchAddress ? "MAC address" : null,
                matchName ? "device name" : null,
                matchCategory ? "category" : null
            };
            Log($"Added {device.Name} to whitelist (match: {string.Join(", ", selectedCriteria.Where(value => value != null))})");
            await RefreshWhitelistAsync();
        }
        catch (Exception ex)
        {
            Log($"Could not add {device.Name} to whitelist: {ex.Message}");
        }
    }

    public async Task RemoveFromWhitelistAsync(WhitelistEntry entry)
    {
        if (_whitelist == null)
        {
            Log("Whitelist storage is unavailable");
            return;
        }

        try
        {
            await _whitelist.DeleteAsync(entry.Id);
            Log($"Removed {entry.FriendlyName} from whitelist");
            await RefreshWhitelistAsync();
        }
        catch (Exception ex)
        {
            Log($"Could not remove {entry.FriendlyName} from whitelist: {ex.Message}");
        }
    }

    public async Task ConnectDeviceAsync(DeviceInfo device)
    {
        if (_agent == null) { Log("Bluetooth agent unavailable"); return; }
        try
        {
            Log($"Requesting connect: {device.Name}");
            var (success, message) = await _agent.ConnectAsync(device.Id);
            Log($"Connect {(success ? "succeeded" : "failed")}: {message}");
        }
        catch (Exception ex)
        {
            Log($"Could not connect to {device.Name}: {ex.Message}");
        }
    }

    public async Task UnpairDeviceAsync(DeviceInfo device)
    {
        if (_agent == null) { Log("Bluetooth agent unavailable"); return; }
        Log($"Requesting unpair: {device.Name}");
        var (success, message) = await _agent.UnpairAsync(device.Id);
        Log($"Unpair {(success ? "succeeded" : "failed")}: {message}");
    }

    private async Task CheckServiceStatusAsync()
    {
        if (_ipcClient == null)
        {
            ServiceStatus = "Disconnected";
            return;
        }
        try
        {
            var response = await _ipcClient.PingAsync();
            ServiceStatus = response?.Status == BlueToothAutoConnect.PolicyEngine.IPC.IpcResponseStatus.Success
                ? "Connected" : "Disconnected";
        }
        catch (Exception ex)
        {
            ServiceStatus = "Disconnected";
            Log($"Service check warning: {ex.Message}");
        }
    }

    private void OnDevicesChanged()
    {
        OnPropertyChanged(nameof(Devices));
        if (!IsDeviceDiscoveryLoading && !_deviceDiscoveryFailed)
            DeviceDiscoveryStatus = $"{Devices.Count} Bluetooth device(s) found.";
    }

    private void OnDiscoveryEnumerationCompleted()
    {
        IsDeviceDiscoveryLoading = false;
        DeviceDiscoveryStatus = Devices.Count == 0
            ? "No Bluetooth devices found yet."
            : $"{Devices.Count} Bluetooth device(s) found.";
    }

    private void OnDiscoveryFailed(string message)
    {
        IsDeviceDiscoveryLoading = false;
        _deviceDiscoveryFailed = true;
        _deviceDiscoveryStarted = false;
        DeviceDiscoveryStatus = "Could not load Bluetooth devices. Select Refresh to try again.";
        Log(message);
    }

    public void Log(string message)
    {
        var entry = $"[{DateTime.Now:HH:mm:ss}] {message}";
        DiagnosticLog.Write($"APP {message}");
        Logs.Insert(0, entry);
        if (Logs.Count > 200) Logs.RemoveAt(Logs.Count - 1);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
