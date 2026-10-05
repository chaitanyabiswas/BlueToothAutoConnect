using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using BlueToothAutoConnect.Persistence.Entities;
using BlueToothAutoConnect.Persistence.Repositories;
using BlueToothAutoConnect.PolicyEngine.IPC;
using BlueToothAutoConnect.PolicyEngine.Models;
using BlueToothAutoConnect.PrivilegedHost.Bluetooth;
using Windows.Devices.Enumeration;

namespace BlueToothAutoConnect.PrivilegedHost.IPC;

public class IpcServer : BackgroundService
{
    public const string PipeName = "BlueToothAutoConnect_PrivilegedHost";
    private readonly BluetoothService _bluetooth;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<IpcServer> _logger;

    public IpcServer(BluetoothService bluetooth, IServiceScopeFactory scopeFactory, ILogger<IpcServer> logger)
    {
        _bluetooth = bluetooth;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("IPC server starting on pipe: {PipeName}", PipeName);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var pipe = CreatePipeServer();
                await pipe.WaitForConnectionAsync(stoppingToken);
                _ = HandleClientAsync(pipe, stoppingToken);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "IPC server error");
                await Task.Delay(1000, stoppingToken);
            }
        }
    }

    private static NamedPipeServerStream CreatePipeServer()
    {
        var security = new PipeSecurity();
        
        var currentUser = WindowsIdentity.GetCurrent().User;
        if (currentUser != null)
        {
            security.AddAccessRule(new PipeAccessRule(
                currentUser,
                PipeAccessRights.FullControl,
                AccessControlType.Allow));
        }

        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            PipeAccessRights.FullControl,
            AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            PipeAccessRights.FullControl,
            AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null),
            PipeAccessRights.ReadWrite,
            AccessControlType.Allow));

        return NamedPipeServerStreamAcl.Create(
            PipeName, PipeDirection.InOut,
            NamedPipeServerStream.MaxAllowedServerInstances,
            PipeTransmissionMode.Message, PipeOptions.Asynchronous,
            0, 0, security);
    }

    private async Task HandleClientAsync(NamedPipeServerStream pipe, CancellationToken ct)
    {
        await using (pipe)
        {
            try
            {
                using var reader = new StreamReader(pipe, leaveOpen: true);
                await using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };

                var line = await reader.ReadLineAsync(ct);
                if (line == null) return;

                var request = JsonSerializer.Deserialize<IpcRequest>(line);
                if (request == null) return;

                _logger.LogInformation("IPC request: {Type} from {User}", request.RequestType, request.UserSid);

                var response = await ProcessRequestAsync(request, ct);
                await writer.WriteLineAsync(JsonSerializer.Serialize(response));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling IPC client");
            }
        }
    }

    private async Task<IpcResponse> ProcessRequestAsync(IpcRequest request, CancellationToken ct)
    {
        var response = new IpcResponse { RequestId = request.RequestId };

        switch (request.RequestType)
        {
            case IpcRequestType.Ping:
                response.Status = IpcResponseStatus.Success;
                response.Message = "Pong";
                break;

            case IpcRequestType.ConnectToDevice:
            {
                if (!request.Parameters.TryGetValue("deviceId", out var deviceIdObj))
                {
                    response.Status = IpcResponseStatus.InvalidParameters;
                    response.Message = "Missing deviceId";
                    break;
                }
                var deviceId = deviceIdObj.ToString()!;
                var (success, message) = await _bluetooth.PairDeviceAsync(deviceId);

                if (success)
                    response.PendingUnpairDeviceIds = await HandleReplaceMatchingDevicesAsync(deviceId, connectionConfirmed: true, ct);

                response.Status = success ? IpcResponseStatus.Success : IpcResponseStatus.Error;
                response.Message = message;
                break;
            }

            case IpcRequestType.FinalizeConnection:
            {
                if (!request.Parameters.TryGetValue("deviceId", out var deviceIdObj))
                {
                    response.Status = IpcResponseStatus.InvalidParameters;
                    response.Message = "Missing deviceId";
                    break;
                }

                var connectionConfirmed = request.Parameters.TryGetValue("connectionConfirmed", out var confirmedValue) &&
                                          bool.TryParse(confirmedValue.ToString(), out var confirmed) && confirmed;
                response.PendingUnpairDeviceIds = await HandleReplaceMatchingDevicesAsync(
                    deviceIdObj.ToString()!, connectionConfirmed, ct);
                response.Status = IpcResponseStatus.Success;
                response.Message = "Whitelist replacement complete";
                break;
            }

            case IpcRequestType.UnpairDevice:
            {
                if (!request.Parameters.TryGetValue("deviceId", out var deviceIdObj))
                {
                    response.Status = IpcResponseStatus.InvalidParameters;
                    response.Message = "Missing deviceId";
                    break;
                }
                var deviceId = deviceIdObj.ToString()!;
                var (success, message) = await _bluetooth.UnpairDeviceAsync(deviceId);
                response.Status = success ? IpcResponseStatus.Success : IpcResponseStatus.Error;
                response.Message = message;
                break;
            }

            default:
                response.Status = IpcResponseStatus.Error;
                response.Message = $"Unknown request type: {request.RequestType}";
                break;
        }

        return response;
    }

    private async Task<List<string>> HandleReplaceMatchingDevicesAsync(string newDeviceId, bool connectionConfirmed, CancellationToken ct)
    {
        try
        {
            var requestedProperties = new[]
            {
                "System.Devices.Aep.DeviceAddress",
                "System.Devices.Aep.IsPaired",
                "System.Devices.Aep.IsConnected"
            };
            var newDevice = await DeviceInformation.CreateFromIdAsync(newDeviceId, requestedProperties);
            if (newDevice == null || !newDevice.Pairing.IsPaired)
            {
                _logger.LogWarning("Cannot replace prior devices because {DeviceId} is not paired", newDeviceId);
                return [];
            }

            var isActive = connectionConfirmed ||
                           (newDevice.Properties.TryGetValue("System.Devices.Aep.IsConnected", out var connectedValue) && connectedValue is true);
            if (!connectionConfirmed && newDeviceId.Contains("BluetoothLE", StringComparison.OrdinalIgnoreCase))
            {
                var bleDevice = await Windows.Devices.Bluetooth.BluetoothLEDevice.FromIdAsync(newDeviceId);
                if (bleDevice == null)
                    return [];

                var gatt = await bleDevice.GetGattServicesAsync(Windows.Devices.Bluetooth.BluetoothCacheMode.Uncached);
                isActive = gatt.Status == Windows.Devices.Bluetooth.GenericAttributeProfile.GattCommunicationStatus.Success ||
                           bleDevice.ConnectionStatus == Windows.Devices.Bluetooth.BluetoothConnectionStatus.Connected;
            }

            if (!isActive)
            {
                _logger.LogWarning("Cannot replace prior devices because {DeviceId} is not currently connected", newDeviceId);
                return [];
            }

            newDevice.Properties.TryGetValue("System.Devices.Aep.DeviceAddress", out var addressValue);
            var address = addressValue?.ToString();
            if (string.IsNullOrWhiteSpace(address))
                return [];

            using var scope = _scopeFactory.CreateScope();
            var whitelistRepo = scope.ServiceProvider.GetRequiredService<IWhitelistRepository>();
            var historyRepo = scope.ServiceProvider.GetRequiredService<IDeviceHistoryRepository>();
            var matchingRule = await whitelistRepo.FindMatchingEntryAsync(
                address, newDevice.Name, DeviceCategoryClassifier.Infer(newDevice.Name));
            var failedDeviceIds = new List<string>();

            if (matchingRule != null)
            {
                const string aqsFilter =
                    "System.Devices.Aep.ProtocolId:=\"{e0cbf06c-cd8b-4647-bb8a-263b43f0f974}\" OR " +
                    "System.Devices.Aep.ProtocolId:=\"{bb7bb05e-5972-42b5-94fc-76eaa7084d49}\"";
                var pairedDevices = await DeviceInformation.FindAllAsync(
                    aqsFilter, requestedProperties, DeviceInformationKind.AssociationEndpoint);

                var replacements = new List<DeviceInformation>();
                foreach (var candidate in pairedDevices)
                {
                    if (candidate.Id.Equals(newDeviceId, StringComparison.OrdinalIgnoreCase) || !candidate.Pairing.IsPaired)
                        continue;

                    candidate.Properties.TryGetValue("System.Devices.Aep.DeviceAddress", out var candidateAddressValue);
                    var candidateAddress = candidateAddressValue?.ToString();
                    if (string.IsNullOrWhiteSpace(candidateAddress))
                        continue;

                    var candidateRule = await whitelistRepo.FindMatchingEntryAsync(
                        candidateAddress, candidate.Name, DeviceCategoryClassifier.Infer(candidate.Name));
                    if (candidateRule?.Id == matchingRule.Id)
                        replacements.Add(candidate);
                }

                if (replacements.Count > 0)
                    await Task.Delay(TimeSpan.FromSeconds(10), ct);

                foreach (var oldDevice in replacements)
                {
                    _logger.LogInformation("Replacing paired device {OldId} with active device {NewId} for whitelist rule {RuleId}",
                        oldDevice.Id, newDeviceId, matchingRule.Id);
                    var (unpaired, message) = await _bluetooth.UnpairDeviceAsync(oldDevice.Id);
                    if (!unpaired)
                    {
                        _logger.LogWarning("Could not unpair replaced device {OldId}: {Message}", oldDevice.Id, message);
                        failedDeviceIds.Add(oldDevice.Id);
                        continue;
                    }

                    var oldHistory = await historyRepo.GetByDeviceIdAsync(oldDevice.Id);
                    if (oldHistory != null)
                    {
                        oldHistory.ReplacedByDeviceId = newDeviceId;
                        oldHistory.ReplacedAt = DateTime.UtcNow;
                        await historyRepo.UpdateAsync(oldHistory);
                    }
                }
            }

            var newEntry = await historyRepo.GetByDeviceIdAsync(newDeviceId)
                ?? new DeviceHistory { DeviceAddress = address, DeviceId = newDeviceId };
            newEntry.DeviceAddress = address;
            newEntry.LastSeen = DateTime.UtcNow;
            newEntry.LastConnectedAt = DateTime.UtcNow;

            if (newEntry.Id == 0)
                await historyRepo.AddAsync(newEntry);
            else
                await historyRepo.UpdateAsync(newEntry);

            return failedDeviceIds;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to replace prior paired devices for {DeviceId}", newDeviceId);
            return [];
        }
    }
}
