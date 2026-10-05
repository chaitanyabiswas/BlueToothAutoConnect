using Windows.Devices.Enumeration;

namespace BlueToothAutoConnect_UserAgent_Cli.Services;

internal static class ConsolePairingPrompt
{
    public static void Handle(DevicePairingRequestedEventArgs args)
    {
        switch (args.PairingKind)
        {
            case DevicePairingKinds.ConfirmOnly:
                args.Accept();
                break;
            case DevicePairingKinds.DisplayPin:
                Console.WriteLine($"Enter this PIN on the Bluetooth device: {args.Pin}");
                args.Accept();
                break;
            case DevicePairingKinds.ProvidePin:
                Console.Write("Enter the PIN displayed by the Bluetooth device: ");
                var pin = Console.ReadLine()?.Trim();
                if (!string.IsNullOrEmpty(pin))
                    args.Accept(pin);
                break;
            case DevicePairingKinds.ConfirmPinMatch:
                Console.WriteLine($"Confirm that {args.Pin} matches the PIN shown on the Bluetooth device.");
                Console.Write("PINs match? [y/N]: ");
                if (string.Equals(Console.ReadLine()?.Trim(), "y", StringComparison.OrdinalIgnoreCase))
                    args.Accept();
                break;
            default:
                Console.WriteLine($"Unsupported Bluetooth pairing ceremony: {args.PairingKind}");
                break;
        }
    }
}
