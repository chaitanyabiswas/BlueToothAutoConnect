using BlueToothAutoConnect_UserAgent.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Windowing;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace BlueToothAutoConnect_UserAgent;

/// <summary>
/// The application window. This hosts a Frame that displays pages. Add your
/// UI and logic to MainPage.xaml / MainPage.xaml.cs instead of here so you
/// can use Page features such as navigation events and the Loaded lifecycle.
/// </summary>
public sealed partial class MainWindow : Window
{
    private readonly SystemTrayIcon _systemTrayIcon;
    private bool _isExiting;

    public MainWindow()
    {
        InitializeComponent();

        Title = "Bluetooth Auto Connect";
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1100, 720));
        AppWindow.Show(true);

        try
        {
            ExtendsContentIntoTitleBar = true;
            SetTitleBar(AppTitleBar);
        }
        catch { }

        var iconPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
        if (System.IO.File.Exists(iconPath))
        {
            try { AppWindow.SetIcon(iconPath); } catch { }
        }

        _systemTrayIcon = new SystemTrayIcon(iconPath);
        _systemTrayIcon.OpenRequested += RestoreFromTray;
        _systemTrayIcon.ExitRequested += ExitApplication;
        AppWindow.Changed += OnAppWindowChanged;
        AppWindow.Closing += OnAppWindowClosing;

        var deviceAdapter = App.Services.GetRequiredService<BluetoothAgentViewModelAdapter>();
        deviceAdapter.WhitelistedDeviceConnected += name =>
            _systemTrayIcon.ShowNotification("Whitelist device connected", $"Connected: {name}");
        deviceAdapter.WhitelistedDeviceDisconnected += name =>
            _systemTrayIcon.ShowNotification("Bluetooth device disconnected", $"Disconnected: {name}");

        // Navigate the root frame to the main page on startup.
        RootFrame.Navigate(typeof(MainPage));
    }

    private void OnAppWindowChanged(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (!args.DidPresenterChange ||
            sender.Presenter is not OverlappedPresenter presenter ||
            presenter.State != OverlappedPresenterState.Minimized)
            return;

        sender.Hide();
        _systemTrayIcon.ShowNotification(
            "Bluetooth Auto Connect is running in the background",
            "Double-click the notification-area icon to reopen the window.");
    }

    private void OnAppWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_isExiting)
            return;

        args.Cancel = true;
        sender.Hide();
        _systemTrayIcon.ShowNotification(
            "Bluetooth Auto Connect is running in the background",
            "The window was hidden. Use the notification-area menu to exit.");
    }

    private void RestoreFromTray()
    {
        if (AppWindow.Presenter is OverlappedPresenter presenter &&
            presenter.State == OverlappedPresenterState.Minimized)
            presenter.Restore();

        AppWindow.Show();
        Activate();
    }

    private void ExitApplication()
    {
        _isExiting = true;
        AppWindow.Changed -= OnAppWindowChanged;
        AppWindow.Closing -= OnAppWindowClosing;
        _systemTrayIcon.Dispose();
        Application.Current.Exit();
    }
}
