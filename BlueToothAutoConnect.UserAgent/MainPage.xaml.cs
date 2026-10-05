using BlueToothAutoConnect.Persistence.Entities;
using BlueToothAutoConnect.PolicyEngine.Models;
using BlueToothAutoConnect_UserAgent.Services;
using BlueToothAutoConnect_UserAgent.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage;

namespace BlueToothAutoConnect_UserAgent;

public sealed partial class MainPage : Page
{
    public MainViewModel ViewModel { get; }

    public MainPage()
    {
        InitializeComponent();
        var logoPath = System.IO.Path.Combine(
            AppContext.BaseDirectory,
            "Assets",
            "BlueToothAutoConnect-logo.png");
        if (System.IO.File.Exists(logoPath))
            AppLogo.Source = new BitmapImage(new Uri(logoPath));

        if (App.Services != null)
        {
            try
            {
                ViewModel = App.Services.GetRequiredService<MainViewModel>();
            }
            catch (System.Exception ex)
            {
                ViewModel = new MainViewModel(null, null, null);
                ViewModel.Log($"DI Service Resolution Warning: {ex.Message}");
            }
        }
        else
        {
            ViewModel = new MainViewModel(null, null, null);
        }

        Loaded += async (_, _) =>
        {
            if (!string.IsNullOrEmpty(App.StartupError))
            {
                ViewModel.Log($"[STARTUP ALERT] {App.StartupError}");
            }
            await ViewModel.InitializeAsync();
        };
    }

    private async void OnRefreshWhitelistClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        await ViewModel.RefreshWhitelistAsync(App.InitializeDatabaseAsync);
    }

    private async void OnRefreshDevicesClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        await ViewModel.RefreshDeviceDiscoveryAsync();
    }

    private async void OnOpenDiagnosticLogsClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(DiagnosticLog.LogDirectory);
            var folder = await StorageFolder.GetFolderFromPathAsync(DiagnosticLog.LogDirectory);
            if (!await Windows.System.Launcher.LaunchFolderAsync(folder))
                ViewModel.Log($"Could not open diagnostic log folder: {DiagnosticLog.LogDirectory}");
        }
        catch (Exception ex)
        {
            DiagnosticLog.WriteException("Opening diagnostic log folder", ex);
            ViewModel.Log($"Could not open diagnostic log folder: {ex.Message}");
        }
    }

    private async void OnTrustDeviceClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (sender is Button { Tag: DeviceInfo device })
        {
            var matchAddress = new CheckBox
            {
                Content = $"MAC address ({device.Address})",
                IsChecked = !string.IsNullOrWhiteSpace(device.Address),
                IsEnabled = !string.IsNullOrWhiteSpace(device.Address)
            };
            var matchName = new CheckBox
            {
                Content = $"Device name ({device.Name})",
                IsChecked = !string.IsNullOrWhiteSpace(device.Name),
                IsEnabled = !string.IsNullOrWhiteSpace(device.Name)
            };
            var matchCategory = new CheckBox
            {
                Content = $"Category ({device.Category})",
                IsChecked = !string.IsNullOrWhiteSpace(device.Category),
                IsEnabled = !string.IsNullOrWhiteSpace(device.Category)
            };

            var criteria = new[] { matchAddress, matchName, matchCategory };
            var dialog = new ContentDialog
            {
                Title = $"Trust {device.Name}",
                Content = new StackPanel
                {
                    Spacing = 8,
                    Children =
                    {
                        new TextBlock { Text = "Select which criteria must match. All checked criteria are required." },
                        matchAddress,
                        matchName,
                        matchCategory
                    }
                },
                PrimaryButtonText = "Trust",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = XamlRoot
            };

            void UpdatePrimaryButtonState(object? _, object __) =>
                dialog.IsPrimaryButtonEnabled = criteria.Any(checkBox => checkBox.IsChecked == true);

            foreach (var checkBox in criteria)
            {
                checkBox.Checked += UpdatePrimaryButtonState;
                checkBox.Unchecked += UpdatePrimaryButtonState;
            }
            dialog.IsPrimaryButtonEnabled = criteria.Any(checkBox => checkBox.IsChecked == true);

            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                await ViewModel.AddToWhitelistAsync(
                    device,
                    matchAddress.IsChecked == true,
                    matchName.IsChecked == true,
                    matchCategory.IsChecked == true);
            }
        }
    }

    private async void OnConnectDeviceClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (sender is Button { Tag: DeviceInfo device })
            await ViewModel.ConnectDeviceAsync(device);
    }

    private async void OnRemoveWhitelistClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (sender is Button { Tag: WhitelistEntry entry })
        {
            var dialog = new ContentDialog
            {
                Title = "Remove from whitelist",
                Content = $"Remove '{entry.FriendlyName}' from trusted devices?",
                PrimaryButtonText = "Remove",
                CloseButtonText = "Cancel",
                XamlRoot = XamlRoot
            };
            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
                await ViewModel.RemoveFromWhitelistAsync(entry);
        }
    }
}
