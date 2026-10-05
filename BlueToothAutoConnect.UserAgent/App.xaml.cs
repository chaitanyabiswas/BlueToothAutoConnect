using BlueToothAutoConnect.Persistence;
using BlueToothAutoConnect.Persistence.Data;
using BlueToothAutoConnect.PolicyEngine;
using BlueToothAutoConnect.AgentCore;
using BlueToothAutoConnect_UserAgent.Services;
using BlueToothAutoConnect_UserAgent.ViewModels;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;

namespace BlueToothAutoConnect_UserAgent;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;
    private Window? _window;
    private static Task? _databaseInitializationTask;

    public static string? StartupError { get; private set; }

    internal static XamlRoot? CurrentXamlRoot
    {
        get
        {
            if (Application.Current is not App app)
                return null;
            return app._window?.Content is FrameworkElement content ? content.XamlRoot : null;
        }
    }

    public App()
    {
        DiagnosticLog.StartSession();
        UnhandledException += (s, e) =>
        {
            DiagnosticLog.WriteException("WinUI Application.UnhandledException",
                e.Exception ?? new Exception(e.Message));
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            DiagnosticLog.Write($"FATAL AppDomain unhandled exception; terminating={e.IsTerminating}{Environment.NewLine}{e.ExceptionObject}");
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            DiagnosticLog.WriteException("TaskScheduler.UnobservedTaskException", e.Exception);
            e.SetObserved();
        };

        try
        {
            InitializeComponent();
        }
        catch (Exception ex)
        {
            StartupError = $"XAML Initialization Failed: {ex.Message}";
            DiagnosticLog.WriteException("XAML initialization", ex);
        }

        try
        {
            var dbPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "BlueToothAutoConnect", "bluetooth.db");
            Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);

            var services = new ServiceCollection();
            services.AddPersistenceServices($"Data Source={dbPath}");
            services.AddPolicyEngineServices();
            services.AddSingleton<AgentIpcClient>();
            services.AddSingleton<BluetoothAgentViewModelAdapter>();
            services.AddTransient<MainViewModel>();
            Services = services.BuildServiceProvider();
        }
        catch (Exception ex)
        {
            StartupError = $"Database/Services Setup Warning: {ex.Message}";
            DiagnosticLog.WriteException("Dependency injection setup", ex);
        }
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _ = InitializeDatabaseAsync();
        var startInBackground =
            args.Arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Contains("--background", StringComparer.OrdinalIgnoreCase) ||
            Environment.GetCommandLineArgs()
                .Skip(1)
                .Contains("--background", StringComparer.OrdinalIgnoreCase);
        EnsureWindowCreated(startInBackground);
    }

    public static Task InitializeDatabaseAsync()
    {
        if (_databaseInitializationTask == null ||
            _databaseInitializationTask.IsFaulted ||
            _databaseInitializationTask.IsCanceled)
        {
            _databaseInitializationTask = InitializeDatabaseCoreAsync();
        }

        return _databaseInitializationTask;
    }

    private static async Task InitializeDatabaseCoreAsync()
    {
        try
        {
            if (Services == null)
                throw new InvalidOperationException(StartupError ?? "Application services are unavailable.");

            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<BluetoothDbContext>();
            await db.Database.EnsureCreatedAsync();
            await db.Database.OpenConnectionAsync();

            try
            {
                var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                await using (var command = db.Database.GetDbConnection().CreateCommand())
                {
                    command.CommandText = "PRAGMA table_info('WhitelistEntries');";
                    await using var reader = await command.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                        columns.Add(reader.GetString(1));
                }

                if (!columns.Contains("DeviceName"))
                    await db.Database.ExecuteSqlRawAsync("ALTER TABLE WhitelistEntries ADD COLUMN DeviceName TEXT NULL;");
                if (!columns.Contains("Category"))
                    await db.Database.ExecuteSqlRawAsync("ALTER TABLE WhitelistEntries ADD COLUMN Category TEXT NULL;");
            }
            finally
            {
                await db.Database.CloseConnectionAsync();
            }
        }
        catch (Exception ex)
        {
            StartupError = $"Database initialization failed: {ex.Message}";
            DiagnosticLog.WriteException("Database initialization", ex);
            throw;
        }
    }

    public void EnsureWindowCreated(bool startInBackground = false)
    {
        if (_window == null)
        {
            try
            {
                _window = new MainWindow(startInBackground);
                if (!startInBackground)
                    _window.Activate();
            }
            catch (Exception ex)
            {
                DiagnosticLog.WriteException("Main window creation", ex);
            }

        }
    }
}
