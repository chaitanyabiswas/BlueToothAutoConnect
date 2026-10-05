using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;

namespace BlueToothAutoConnect.AgentCore;

public sealed class UserSessionBluetoothService
{
    private readonly Action<DevicePairingRequestedEventArgs>? _pairingRequested;

    public UserSessionBluetoothService(Action<DevicePairingRequestedEventArgs>? pairingRequested = null)
    {
        _pairingRequested = pairingRequested;
    }

    public async Task<(bool Success, string Message)> ConnectAsync(string deviceId)
    {
        try
        {
            var device = await DeviceInformation.CreateFromIdAsync(deviceId);
            if (device == null)
                return (false, "Device not found in the signed-in user session");

            var pairStatus = DevicePairingResultStatus.AlreadyPaired;
            if (!device.Pairing.IsPaired)
            {
                var pairResult = await PairAsync(device);
                pairStatus = pairResult.Status;
                if (pairStatus is not (DevicePairingResultStatus.Paired or DevicePairingResultStatus.AlreadyPaired))
                    return (false, $"Windows user-session pairing returned {pairStatus}");
            }

            if (deviceId.Contains("BluetoothLE", StringComparison.OrdinalIgnoreCase))
            {
                var bleDevice = await BluetoothLEDevice.FromIdAsync(deviceId);
                if (bleDevice == null)
                    return (false, $"Paired in the signed-in user session ({pairStatus}), but BLE connection initialization failed");

                var gattStatus = GattCommunicationStatus.Unreachable;
                for (var attempt = 0; attempt < 3; attempt++)
                {
                    var gattResult = await bleDevice.GetGattServicesAsync(BluetoothCacheMode.Uncached);
                    gattStatus = gattResult.Status;
                    if (gattStatus == GattCommunicationStatus.Success ||
                        bleDevice.ConnectionStatus == BluetoothConnectionStatus.Connected)
                    {
                        return (true, $"Paired in the signed-in user session ({pairStatus}); GATT status: {gattStatus}");
                    }

                    if (attempt < 2)
                        await Task.Delay(TimeSpan.FromSeconds(1));
                }

                return (false, $"Paired in the signed-in user session ({pairStatus}), but the device is not reachable (GATT status: {gattStatus})");
            }

            return (true, $"Paired in the signed-in user session ({pairStatus})");
        }
        catch (Exception ex)
        {
            return (false, $"User-session pairing error: {ex.GetType().Name} - {ex.Message}");
        }
    }

    public async Task<(bool Success, string Message)> UnpairAsync(string deviceId)
    {
        try
        {
            var device = await DeviceInformation.CreateFromIdAsync(deviceId);
            if (device == null)
                return (false, "Device not found in the signed-in user session");
            if (!device.Pairing.IsPaired)
                return (true, "Already unpaired");

            var result = await device.Pairing.UnpairAsync();
            var success = result.Status is DeviceUnpairingResultStatus.Unpaired or DeviceUnpairingResultStatus.AlreadyUnpaired;
            return (success, $"User-session unpair returned {result.Status}");
        }
        catch (Exception ex)
        {
            return (false, $"User-session unpair error: {ex.GetType().Name} - {ex.Message}");
        }
    }

    private async Task<DevicePairingResult> PairAsync(DeviceInformation device)
    {
        var customPairing = device.Pairing.Custom;
        if (customPairing == null)
            return await device.Pairing.PairAsync();

        void HandlePairingRequested(DeviceInformationCustomPairing sender, DevicePairingRequestedEventArgs args)
        {
            if (_pairingRequested != null)
            {
                _pairingRequested(args);
            }
            else if (args.PairingKind == DevicePairingKinds.ConfirmOnly)
            {
                args.Accept();
            }
        }

        customPairing.PairingRequested += HandlePairingRequested;
        try
        {
            var supportedPairingKinds = DevicePairingKinds.ConfirmOnly |
                                        DevicePairingKinds.DisplayPin |
                                        DevicePairingKinds.ProvidePin |
                                        DevicePairingKinds.ConfirmPinMatch;
            return await customPairing.PairAsync(supportedPairingKinds, DevicePairingProtectionLevel.Default);
        }
        finally
        {
            customPairing.PairingRequested -= HandlePairingRequested;
        }
    }
}
