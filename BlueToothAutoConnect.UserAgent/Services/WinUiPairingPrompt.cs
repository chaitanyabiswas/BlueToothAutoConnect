using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Controls;
using Windows.Devices.Enumeration;

namespace BlueToothAutoConnect_UserAgent.Services;

internal sealed class WinUiPairingPrompt
{
    private readonly DispatcherQueue _dispatcherQueue;

    public WinUiPairingPrompt(DispatcherQueue dispatcherQueue)
    {
        _dispatcherQueue = dispatcherQueue;
    }

    public void Handle(DevicePairingRequestedEventArgs args)
    {
        if (args.PairingKind == DevicePairingKinds.ConfirmOnly)
        {
            args.Accept();
            return;
        }

        using var completed = new ManualResetEventSlim();
        _dispatcherQueue.TryEnqueue(async () =>
        {
            try
            {
                var xamlRoot = App.CurrentXamlRoot;
                if (xamlRoot == null)
                    return;

                var dialog = new ContentDialog
                {
                    XamlRoot = xamlRoot,
                    CloseButtonText = "Cancel"
                };

                TextBox? pinInput = null;
                switch (args.PairingKind)
                {
                    case DevicePairingKinds.DisplayPin:
                        dialog.Title = "Pair Bluetooth device";
                        dialog.Content = $"Enter this PIN on the Bluetooth device: {args.Pin}";
                        dialog.PrimaryButtonText = "Continue";
                        break;
                    case DevicePairingKinds.ProvidePin:
                        dialog.Title = "Bluetooth PIN required";
                        dialog.Content = pinInput = new TextBox { PlaceholderText = "PIN" };
                        dialog.PrimaryButtonText = "Pair";
                        break;
                    case DevicePairingKinds.ConfirmPinMatch:
                        dialog.Title = "Confirm Bluetooth PIN";
                        dialog.Content = $"Does {args.Pin} match the PIN shown on the device?";
                        dialog.PrimaryButtonText = "Yes";
                        break;
                    default:
                        return;
                }

                if (await dialog.ShowAsync() == ContentDialogResult.Primary)
                {
                    if (args.PairingKind == DevicePairingKinds.ProvidePin)
                    {
                        if (!string.IsNullOrWhiteSpace(pinInput?.Text))
                            args.Accept(pinInput.Text.Trim());
                    }
                    else
                    {
                        args.Accept();
                    }
                }
            }
            finally
            {
                completed.Set();
            }
        });
        completed.Wait();
    }
}
