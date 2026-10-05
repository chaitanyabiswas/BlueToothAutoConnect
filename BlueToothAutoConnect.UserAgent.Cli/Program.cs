using BlueToothAutoConnect.Persistence;
using BlueToothAutoConnect.Persistence.Data;
using BlueToothAutoConnect.Persistence.Entities;
using BlueToothAutoConnect.Persistence.Repositories;
using BlueToothAutoConnect.PolicyEngine;
using BlueToothAutoConnect.PolicyEngine.Policy;
using BlueToothAutoConnect.AgentCore;
using BlueToothAutoConnect_UserAgent_Cli.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BlueToothAutoConnect_UserAgent_Cli;

public class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length == 0 || args[0] == "-h" || args[0] == "--help" || args[0] == "help")
        {
            ShowHelp();
            return 0;
        }

        var dataDirectory = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        var dbPath = Path.Combine(dataDirectory, "BlueToothAutoConnect", "bluetooth.db");
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);

        var services = new ServiceCollection();
        services.AddPersistenceServices($"Data Source={dbPath}");
        services.AddPolicyEngineServices();
        services.AddSingleton<AgentIpcClient>();

        var serviceProvider = services.BuildServiceProvider();

        // Ensure database is initialized and migrated
        using (var scope = serviceProvider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BluetoothDbContext>();
            db.Database.EnsureCreated();

            // Auto-migrate newly added columns and remove legacy unique constraint if table already existed
            try
            {
                db.Database.ExecuteSqlRaw("ALTER TABLE WhitelistEntries ADD COLUMN DeviceName TEXT NULL;");
            }
            catch { /* Column already exists */ }

            try
            {
                db.Database.ExecuteSqlRaw("ALTER TABLE WhitelistEntries ADD COLUMN Category TEXT NULL;");
            }
            catch { /* Column already exists */ }

            try
            {
                db.Database.ExecuteSqlRaw("DROP INDEX IF EXISTS IX_WhitelistEntries_DeviceAddress;");
                db.Database.ExecuteSqlRaw("CREATE INDEX IF NOT EXISTS IX_WhitelistEntries_DeviceAddress ON WhitelistEntries(DeviceAddress);");
            }
            catch { /* Index already migrated */ }
        }

        var ipcClient = serviceProvider.GetRequiredService<AgentIpcClient>();
        var command = args[0].ToLowerInvariant();

        switch (command)
        {
            case "ping":
            case "status":
                return await HandlePingAsync(ipcClient);

            case "list":
            case "scan":
                int timeoutSec = 5;
                if (args.Length >= 3 && (args[1] == "--timeout" || args[1] == "-t") && int.TryParse(args[2], out var parsedTimeout))
                {
                    timeoutSec = parsedTimeout;
                }
                return await HandleListAsync(timeoutSec);

            case "whitelist":
                return await HandleWhitelistAsync(serviceProvider, args.Skip(1).ToArray());

            case "connect":
                if (args.Length < 2)
                {
                    Console.WriteLine("Error: Missing device address or ID. Usage: connect <address|deviceId>");
                    return 1;
                }
                return await HandleConnectAsync(ipcClient, args[1]);

            case "unpair":
                if (args.Length < 2)
                {
                    Console.WriteLine("Error: Missing device address or ID. Usage: unpair <address|deviceId>");
                    return 1;
                }
                return await HandleUnpairAsync(ipcClient, args[1]);

            case "daemon":
            case "watch":
            case "run":
                return await HandleDaemonAsync(serviceProvider);

            case "--version":
            case "-v":
            case "version":
                PrintVersion();
                return 0;

            default:
                Console.WriteLine($"Unknown command: '{command}'");
                ShowHelp();
                return 1;
        }
    }

    private static void PrintVersion()
    {
        var asm = System.Reflection.Assembly.GetExecutingAssembly();
        var ver = asm.GetName().Version?.ToString(3) ?? "1.0.0";
        var infoVer = System.Reflection.CustomAttributeExtensions.GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>(asm)?.InformationalVersion;
        var displayVer = !string.IsNullOrEmpty(infoVer) ? infoVer.Split('+')[0] : ver;
        Console.WriteLine($"BlueToothAutoConnect CLI User Agent v{displayVer}");
    }

    private static void ShowHelp()
    {
        Console.WriteLine("=========================================================");
        Console.WriteLine(" BlueToothAutoConnect CLI User Agent");
        Console.WriteLine("=========================================================");
        Console.WriteLine("Usage:");
        Console.WriteLine("  BlueToothAutoConnect.UserAgent.Cli.exe <command> [options]\n");
        Console.WriteLine("Commands:");
        Console.WriteLine("  ping / status                           Check Windows service");
        Console.WriteLine("  list / scan [--timeout <seconds>]       Scan discovered Bluetooth devices (default 5s)");
        Console.WriteLine("  whitelist list                          List all whitelisted device rules");
        Console.WriteLine("  whitelist add                           Scan a device and select matching criteria");
        Console.WriteLine("  whitelist add [--mac <addr>] [--name <name>] [--category <cat>]");
        Console.WriteLine("                                          Supplied criteria are all required to match");
        Console.WriteLine("  whitelist remove <id>                   Remove a whitelist rule by its database ID");
        Console.WriteLine("  connect <address | deviceId>            Request connection to device");
        Console.WriteLine("  unpair <address | deviceId>             Request unpairing device");
        Console.WriteLine("  daemon / watch                          Run background policy watcher loop");
        Console.WriteLine("  version / --version / -v                Show version information\n");
    }

    private static async Task<int> HandlePingAsync(AgentIpcClient ipcClient)
    {
        Console.WriteLine("Pinging PrivilegedHost service...");
        var res = await ipcClient.PingAsync();
        if (res != null && res.Status == BlueToothAutoConnect.PolicyEngine.IPC.IpcResponseStatus.Success)
        {
            Console.WriteLine($"[SUCCESS] PrivilegedHost service is running. Response: {res.Message}");
            return 0;
        }
        else
        {
            Console.WriteLine($"[FAILED] PrivilegedHost service unreachable or returned error: {res?.Message}");
            return 1;
        }
    }

    private static async Task<int> HandleListAsync(int timeoutSec)
    {
        Console.WriteLine($"Scanning for Bluetooth devices ({timeoutSec}s)...");
        using var watcher = new CliBluetoothAgent();
        watcher.Start();

        await Task.Delay(TimeSpan.FromSeconds(timeoutSec));

        watcher.Stop();
        var devices = watcher.DiscoveredDevices;

        Console.WriteLine($"\nDiscovered {devices.Count} device(s):");
        Console.WriteLine(new string('-', 85));
        Console.WriteLine($"{"Address",-18} | {"Connected",-9} | {"Connectable",-11} | {"Paired",-6} | {"Name"}");
        Console.WriteLine(new string('-', 85));

        foreach (var dev in devices)
        {
            var addr = string.IsNullOrEmpty(dev.Address) ? "<unknown>" : dev.Address;
            var name = string.IsNullOrEmpty(dev.Name) ? "<unnamed>" : dev.Name;
            Console.WriteLine($"{addr,-18} | {dev.IsConnected,-9} | {dev.IsConnectable,-11} | {dev.IsPaired,-6} | {name}");
        }

        Console.WriteLine(new string('-', 85));
        return 0;
    }

    private static async Task<int> HandleWhitelistAsync(IServiceProvider serviceProvider, string[] subArgs)
    {
        if (subArgs.Length == 0 || subArgs[0].Equals("list", StringComparison.OrdinalIgnoreCase))
        {
            using var scope = serviceProvider.CreateScope();
            var repo = scope.ServiceProvider.GetRequiredService<IWhitelistRepository>();
            var entries = await repo.GetAllActiveAsync();
            Console.WriteLine($"Active whitelist rules: {entries.Count}");
            foreach (var entry in entries)
            {
                var criteria = string.Join(", ", new[]
                {
                    string.IsNullOrWhiteSpace(entry.DeviceAddress) ? null : "MAC",
                    string.IsNullOrWhiteSpace(entry.DeviceName) ? null : "name",
                    string.IsNullOrWhiteSpace(entry.Category) ? null : "category"
                }.Where(value => value != null));
                Console.WriteLine($"#{entry.Id} {entry.FriendlyName} | Match by: {criteria} | MAC: {entry.DeviceAddress} | Name: {entry.DeviceName} | Category: {entry.Category}");
            }
            return 0;
        }

        var action = subArgs[0].ToLowerInvariant();
        if (action == "add")
        {
            string? mac = null;
            string? name = null;
            string? category = null;
            string? description = null;

            if (subArgs.Length == 1)
            {
                Console.WriteLine("Scanning for available Bluetooth devices (5s)...");
                using var watcher = new CliBluetoothAgent();
                watcher.Start();
                await Task.Delay(TimeSpan.FromSeconds(5));
                watcher.Stop();

                var devices = watcher.DiscoveredDevices.ToList();
                if (devices.Count == 0)
                {
                    Console.WriteLine("No devices discovered during scan.");
                    return 1;
                }

                for (var index = 0; index < devices.Count; index++)
                {
                    var device = devices[index];
                    Console.WriteLine($"{index + 1}. {device.Address} | {device.Category} | {device.Name}");
                }
                Console.Write("Select device number: ");
                if (!int.TryParse(Console.ReadLine(), out var selectedIndex) || selectedIndex < 1 || selectedIndex > devices.Count)
                {
                    Console.WriteLine("Invalid device selection.");
                    return 1;
                }

                var chosen = devices[selectedIndex - 1];
                description = chosen.Name;

                Console.WriteLine("Select matching criteria (press Enter to include; all selected criteria must match):");
                if (PromptForCriterion("MAC address", chosen.Address))
                    mac = chosen.Address;
                if (PromptForCriterion("Device name", chosen.Name))
                    name = chosen.Name;
                if (PromptForCriterion("Category", chosen.Category))
                    category = chosen.Category;
            }
            else
            {
                for (var index = 1; index < subArgs.Length; index++)
                {
                    var flag = subArgs[index];
                    if (index + 1 >= subArgs.Length)
                    {
                        Console.WriteLine($"Missing value for {flag}");
                        return 1;
                    }

                    var value = subArgs[++index].Trim();
                    switch (flag.ToLowerInvariant())
                    {
                        case "--mac":
                        case "-m":
                            mac = value;
                            break;
                        case "--name":
                        case "-n":
                            name = value;
                            break;
                        case "--category":
                        case "-c":
                            category = value;
                            break;
                        case "--desc":
                        case "-d":
                            description = value;
                            break;
                        default:
                            Console.WriteLine($"Unknown whitelist option: {flag}");
                            return 1;
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(mac) && string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(category))
            {
                Console.WriteLine("Specify at least one rule criterion: --mac, --name, or --category.");
                return 1;
            }

            description ??= string.Join(" / ", new[] { mac, name, category }.Where(value => !string.IsNullOrWhiteSpace(value)));
            try
            {
                using var scope = serviceProvider.CreateScope();
                var repo = scope.ServiceProvider.GetRequiredService<IWhitelistRepository>();
                var entry = await repo.AddAsync(new WhitelistEntry
                {
                    DeviceAddress = string.IsNullOrWhiteSpace(mac) ? string.Empty : mac.Trim().ToLowerInvariant(),
                    DeviceName = string.IsNullOrWhiteSpace(name) ? null : name.Trim(),
                    Category = string.IsNullOrWhiteSpace(category) ? null : category.Trim(),
                    FriendlyName = description,
                    AddedByUser = true,
                    AddedAt = DateTime.UtcNow,
                    IsActive = true
                });
                Console.WriteLine($"[SUCCESS] Created whitelist rule #{entry.Id}: {entry.FriendlyName}");
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Failed to add whitelist entry: {ex.Message}");
                return 1;
            }
        }

        if (action == "remove" || action == "delete")
        {
            if (subArgs.Length < 2)
            {
                Console.WriteLine("Error: Missing rule ID or target. Usage: whitelist remove <id | address | name>");
                return 1;
            }

            var target = subArgs[1].Trim();
            using var scope = serviceProvider.CreateScope();
            var repo = scope.ServiceProvider.GetRequiredService<IWhitelistRepository>();
            var entries = await repo.GetAllActiveAsync();
            var matched = int.TryParse(target, out var id)
                ? entries.FirstOrDefault(entry => entry.Id == id)
                : entries.FirstOrDefault(entry =>
                    string.Equals(entry.DeviceAddress, target, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(entry.DeviceName, target, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(entry.FriendlyName, target, StringComparison.OrdinalIgnoreCase));

            if (matched == null)
            {
                Console.WriteLine($"[INFO] No whitelist rule found matching '{target}'.");
                return 0;
            }

            await repo.DeleteAsync(matched.Id);
            Console.WriteLine($"[SUCCESS] Removed whitelist rule #{matched.Id} ('{matched.FriendlyName}').");
            return 0;
        }

        Console.WriteLine($"Unknown whitelist command: '{subArgs[0]}'. Use: list, add, remove");
        return 1;
    }

    private static bool PromptForCriterion(string label, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            Console.WriteLine($"  {label}: unavailable (cannot select)");
            return false;
        }

        while (true)
        {
            Console.Write($"  Match {label} '{value}'? [Y/n]: ");
            var response = Console.ReadLine()?.Trim();
            if (string.IsNullOrEmpty(response) ||
                response.Equals("y", StringComparison.OrdinalIgnoreCase) ||
                response.Equals("yes", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            if (response.Equals("n", StringComparison.OrdinalIgnoreCase) ||
                response.Equals("no", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            Console.WriteLine("Please answer Y or N.");
        }
    }

    private static async Task<string> ResolveTargetToDeviceIdAsync(string target)
    {
        if (target.Contains("Bluetooth", StringComparison.OrdinalIgnoreCase) ||
            (!target.Contains(':') && !target.Contains('-') && target.Length != 12))
        {
            return target;
        }

        Console.WriteLine($"Resolving MAC address '{target}' to Bluetooth Device ID...");
        using var watcher = new CliBluetoothAgent();
        watcher.Start();
        await Task.Delay(4000);
        watcher.Stop();

        var normalizedTarget = target.Replace(":", "").Replace("-", "").ToUpperInvariant();
        var matches = watcher.DiscoveredDevices
            .Where(d => !string.IsNullOrEmpty(d.Address) &&
                        d.Address.Replace(":", "").Replace("-", "").ToUpperInvariant() == normalizedTarget)
            .ToList();

        if (matches.Count > 0)
        {
            // Pick paired first, then connectable, then any
            var best = matches.FirstOrDefault(d => d.IsPaired) ??
                       matches.FirstOrDefault(d => d.IsConnectable) ??
                       matches.First();

            Console.WriteLine($"Resolved '{target}' => Device ID: {best.Id} (Paired: {best.IsPaired}, Connectable: {best.IsConnectable})");
            return best.Id;
        }

        Console.WriteLine($"Warning: Could not find live discovered device matching address '{target}'. Sending address directly to PrivilegedHost.");
        return target;
    }

    private static async Task<int> HandleConnectAsync(AgentIpcClient ipcClient, string target)
    {
        string deviceId = await ResolveTargetToDeviceIdAsync(target);

        Console.WriteLine($"Sending connect request for device: {deviceId} ...");
        using var agent = new CliBluetoothAgent(ipcClient: ipcClient);
        var (success, message) = await agent.ConnectAsync(deviceId);
        Console.WriteLine($"{(success ? "[SUCCESS]" : "[ERROR]")} {message}");
        return success ? 0 : 1;
    }

    private static async Task<int> HandleUnpairAsync(AgentIpcClient ipcClient, string target)
    {
        string deviceId = await ResolveTargetToDeviceIdAsync(target);

        Console.WriteLine($"Sending unpair request for device: {deviceId} ...");
        using var agent = new CliBluetoothAgent(ipcClient: ipcClient);
        var (success, message) = await agent.UnpairAsync(deviceId);
        Console.WriteLine($"{(success ? "[SUCCESS]" : "[ERROR]")} {message}");
        return success ? 0 : 1;
    }

    private static async Task<int> HandleDaemonAsync(IServiceProvider serviceProvider)
    {
        Console.WriteLine("Starting Bluetooth Auto Connect CLI User Agent Daemon...");
        Console.WriteLine("Press Ctrl+C to terminate daemon.");

        using var scope = serviceProvider.CreateScope();
        var policyEngine = scope.ServiceProvider.GetRequiredService<IPolicyEngine>();
        var ipcClient = scope.ServiceProvider.GetRequiredService<AgentIpcClient>();

        using var watcher = new CliBluetoothAgent(policyEngine, ipcClient, autoConnect: true);
        watcher.DeviceDiscovered += dev => Console.WriteLine($"[Discovered] {dev.Name} ({dev.Address}) - Connected: {dev.IsConnected}, Connectable: {dev.IsConnectable}");
        watcher.DeviceUpdated += dev => Console.WriteLine($"[Updated] {dev.Name} ({dev.Address}) - Connected: {dev.IsConnected}, Connectable: {dev.IsConnectable}");
        watcher.DeviceRemoved += id => Console.WriteLine($"[Removed] Device ID: {id}");
        watcher.LogMessage += message => Console.WriteLine($"[Daemon] {message}");

        watcher.Start();

        var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (s, e) =>
        {
            e.Cancel = true;
            Console.WriteLine("\nStopping daemon...");
            cts.Cancel();
        };

        try
        {
            await Task.Delay(-1, cts.Token);
        }
        catch (TaskCanceledException)
        {
            // Expected on exit
        }

        watcher.Stop();
        Console.WriteLine("Daemon stopped.");
        return 0;
    }
}
