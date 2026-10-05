using Windows.Devices.Enumeration;

namespace BlueToothAutoConnect.PrivilegedHost.Bluetooth;

public class BluetoothService
{
    private readonly ILogger<BluetoothService> _logger;

    public BluetoothService(ILogger<BluetoothService> logger) => _logger = logger;

    public async Task<(bool Success, string Message)> PairDeviceAsync(string deviceId)
    {
        try
        {
            var props = new[] { "System.Devices.Aep.DeviceAddress" };
            var device = await DeviceInformation.CreateFromIdAsync(deviceId, props);
            if (device == null) return (false, "Device not found");

            device.Properties.TryGetValue("System.Devices.Aep.DeviceAddress", out var targetAddrObj);
            var targetAddress = targetAddrObj?.ToString();

            // If not already paired, clear any existing stale pairing record for this address.
            if (!device.Pairing.IsPaired)
            {
                await FindAndUnpairConflictingPairedDevicesAsync(deviceId, targetAddress);
            }

            // If already paired, ensure connection is triggered for BLE devices
            if (device.Pairing.IsPaired)
            {
                if (deviceId.Contains("BluetoothLE", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        var bleDevice = await Windows.Devices.Bluetooth.BluetoothLEDevice.FromIdAsync(deviceId);
                        if (bleDevice != null)
                        {
                            var gatt = await bleDevice.GetGattServicesAsync(Windows.Devices.Bluetooth.BluetoothCacheMode.Uncached);
                            _logger.LogInformation("Connected to paired BLE device {DeviceId}, GATT status: {Status}", deviceId, gatt.Status);
                            if (gatt.Status == Windows.Devices.Bluetooth.GenericAttributeProfile.GattCommunicationStatus.Success)
                            {
                                return (true, "Already paired and connected via GATT");
                            }
                            return (false, $"Already paired but device unreachable (GATT status: {gatt.Status})");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "BLE GATT connection attempt failed for paired device {DeviceId}", deviceId);
                        return (false, $"GATT connection error: {ex.Message}");
                    }
                }
                return (true, "Already paired");
            }

            if (!device.Pairing.CanPair)
            {
                // For BLE devices, try connecting directly even if CanPair reports false
                if (deviceId.Contains("BluetoothLE", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        var ble = await Windows.Devices.Bluetooth.BluetoothLEDevice.FromIdAsync(deviceId);
                        if (ble != null)
                        {
                            var gatt = await ble.GetGattServicesAsync(Windows.Devices.Bluetooth.BluetoothCacheMode.Uncached);
                            if (gatt.Status == Windows.Devices.Bluetooth.GenericAttributeProfile.GattCommunicationStatus.Success)
                            {
                                _logger.LogInformation("Direct BLE GATT connection succeeded for {DeviceId}", deviceId);
                                return (true, "Connected via GATT");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "Direct BLE GATT check failed for non-pairable device {DeviceId}", deviceId);
                    }
                }

                _logger.LogWarning("Device {DeviceId} cannot be paired (CanPair is false, ProtectionLevel: {ProtectionLevel}).",
                    deviceId, device.Pairing.ProtectionLevel);
                return (false, "Device reports CanPair=false (Device may not be in pairing mode or requires manual discovery in Windows Settings)");
            }

            // Use CustomPairing with automatic acceptance of confirmation/pairing requests
            DevicePairingResultStatus pairStatus;
            var customPairing = device.Pairing.Custom;
            if (customPairing != null)
            {
                void Handler(DeviceInformationCustomPairing sender, DevicePairingRequestedEventArgs args)
                {
                    _logger.LogInformation("PairingRequested for {DeviceId} with kind {Kind}, pin={Pin}", deviceId, args.PairingKind, args.Pin);
                    args.Accept();
                }

                customPairing.PairingRequested += Handler;
                try
                {
                    // Attempt with ProtectionLevel.None first (allows BLE Just Works pairing without PIN)
                    var customResult = await customPairing.PairAsync(
                        DevicePairingKinds.ConfirmOnly | DevicePairingKinds.ProvidePin | DevicePairingKinds.ConfirmPinMatch,
                        DevicePairingProtectionLevel.None);
                    pairStatus = customResult.Status;

                    // If pairing failed, retry without custom pairing or clear conflicts and retry
                    if (pairStatus == DevicePairingResultStatus.Failed)
                    {
                        _logger.LogWarning("CustomPairAsync returned Failed for {DeviceId}. Checking for conflicting device associations to clear...", deviceId);
                        var removed = await FindAndUnpairConflictingPairedDevicesAsync(deviceId, targetAddress);
                        if (removed > 0)
                        {
                            await Task.Delay(600);
                        }

                        // Try standard PairAsync with ProtectionLevel.None
                        var stdResult = await device.Pairing.PairAsync(DevicePairingProtectionLevel.None);
                        pairStatus = stdResult.Status;
                        _logger.LogInformation("Standard PairAsync retry for {DeviceId}: {Status}", deviceId, pairStatus);
                    }
                }
                finally
                {
                    customPairing.PairingRequested -= Handler;
                }
            }
            else
            {
                var standardResult = await device.Pairing.PairAsync(DevicePairingProtectionLevel.None);
                pairStatus = standardResult.Status;
            }

            var success = pairStatus is DevicePairingResultStatus.Paired or DevicePairingResultStatus.AlreadyPaired;
            _logger.LogInformation("Pair {DeviceId}: {Status}", deviceId, pairStatus);

            if (success && deviceId.Contains("BluetoothLE", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var bleDevice = await Windows.Devices.Bluetooth.BluetoothLEDevice.FromIdAsync(deviceId);
                    if (bleDevice != null)
                    {
                        var gattResult = await bleDevice.GetGattServicesAsync(Windows.Devices.Bluetooth.BluetoothCacheMode.Uncached);
                        _logger.LogInformation("Post-pairing GATT service query for {DeviceId}: {Status}", deviceId, gattResult.Status);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to initialize post-pairing GATT services for {DeviceId}", deviceId);
                }
            }

            if (!success)
            {
                var explanation = pairStatus switch
                {
                    DevicePairingResultStatus.Failed => "Failed (Device may be actively connected to another host, out of pairing window, or rejected pairing negotiation)",
                    DevicePairingResultStatus.ConnectionRejected => "Connection rejected by device",
                    DevicePairingResultStatus.NotReadyToPair => "Device not ready to pair (Put mouse in pairing mode by holding pairing button)",
                    DevicePairingResultStatus.AccessDenied => "Access denied (Host permissions or policy restriction)",
                    DevicePairingResultStatus.NoSupportedProfiles => "No supported Bluetooth profiles found",
                    DevicePairingResultStatus.AuthenticationFailure => "Authentication failed during pairing",
                    DevicePairingResultStatus.AuthenticationTimeout => "Authentication timed out",
                    DevicePairingResultStatus.OperationAlreadyInProgress => "Pairing operation already in progress",
                    _ => pairStatus.ToString()
                };
                return (false, explanation);
            }

            return (true, pairStatus.ToString());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PairDevice failed for {DeviceId}", deviceId);
            return (false, $"Exception: {ex.GetType().Name} - {ex.Message}");
        }
    }

    public async Task<(bool Success, string Message)> UnpairDeviceAsync(string deviceId)
    {
        try
        {
            var device = await DeviceInformation.CreateFromIdAsync(deviceId);
            if (device == null) return (false, "Device not found");
            if (!device.Pairing.IsPaired) return (true, "Not paired");

            var result = await device.Pairing.UnpairAsync();
            var success = result.Status is DeviceUnpairingResultStatus.Unpaired or DeviceUnpairingResultStatus.AlreadyUnpaired;
            _logger.LogInformation("Unpair {DeviceId}: {Status}", deviceId, result.Status);
            return (success, result.Status.ToString());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "UnpairDevice failed for {DeviceId}", deviceId);
            return (false, ex.Message);
        }
    }

    public async Task<string?> GetDeviceAddressAsync(string deviceId)
    {
        try
        {
            var props = new[] { "System.Devices.Aep.DeviceAddress" };
            var device = await DeviceInformation.CreateFromIdAsync(deviceId, props);
            device.Properties.TryGetValue("System.Devices.Aep.DeviceAddress", out var addr);
            return addr?.ToString();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetDeviceAddress failed for {DeviceId}", deviceId);
            return null;
        }
    }

    public async Task<int> FindAndUnpairConflictingPairedDevicesAsync(string targetDeviceId, string? targetAddress)
    {
        int unpairCount = 0;
        try
        {
            const string aqsFilter =
                "System.Devices.Aep.ProtocolId:=\"{e0cbf06c-cd8b-4647-bb8a-263b43f0f974}\" OR " +
                "System.Devices.Aep.ProtocolId:=\"{bb7bb05e-5972-42b5-94fc-76eaa7084d49}\"";

            var requestedProperties = new[]
            {
                "System.Devices.Aep.DeviceAddress",
                "System.Devices.Aep.IsPaired"
            };

            var devices = await DeviceInformation.FindAllAsync(aqsFilter, requestedProperties, DeviceInformationKind.AssociationEndpoint);

            var normalizedTargetAddr = targetAddress?.Replace(":", "").Replace("-", "").ToUpperInvariant();

            foreach (var dev in devices)
            {
                if (dev.Id.Equals(targetDeviceId, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!dev.Pairing.IsPaired)
                {
                    dev.Properties.TryGetValue("System.Devices.Aep.IsPaired", out var pObj);
                    if (pObj is not true)
                        continue;
                }

                if (!string.IsNullOrEmpty(normalizedTargetAddr))
                {
                    dev.Properties.TryGetValue("System.Devices.Aep.DeviceAddress", out var devAddrObj);
                    var devAddr = devAddrObj?.ToString()?.Replace(":", "").Replace("-", "").ToUpperInvariant();
                    if (!string.IsNullOrEmpty(devAddr) && devAddr == normalizedTargetAddr)
                    {
                        _logger.LogInformation("Found a paired association for Bluetooth address {Address} ({DeviceId}). Unpairing it before retry...", targetAddress, dev.Id);
                        var (unpaired, msg) = await UnpairDeviceAsync(dev.Id);
                        if (unpaired)
                        {
                            unpairCount++;
                            _logger.LogInformation("Successfully unpaired association {DeviceId}: {Msg}", dev.Id, msg);
                        }
                        else
                        {
                            _logger.LogWarning("Failed to unpair association {DeviceId}: {Msg}", dev.Id, msg);
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking for conflicting paired devices for {DeviceId}", targetDeviceId);
        }

        return unpairCount;
    }
}
