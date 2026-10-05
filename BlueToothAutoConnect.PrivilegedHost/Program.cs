using BlueToothAutoConnect.Persistence;
using BlueToothAutoConnect.PolicyEngine;
using BlueToothAutoConnect.PrivilegedHost.Bluetooth;
using BlueToothAutoConnect.PrivilegedHost.IPC;
using Microsoft.EntityFrameworkCore;

var builder = Host.CreateApplicationBuilder(args);

var dbPath = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
    "BlueToothAutoConnect", "bluetooth.db");
Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);

builder.Services.AddPersistenceServices($"Data Source={dbPath}");
builder.Services.AddPolicyEngineServices();
builder.Services.AddSingleton<BluetoothService>();
builder.Services.AddHostedService<IpcServer>();

builder.Services.AddWindowsService(options =>
    options.ServiceName = "BlueToothAutoConnect Privileged Host");

var host = builder.Build();

// Ensure DB is created and migrated
using (var scope = host.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<BlueToothAutoConnect.Persistence.Data.BluetoothDbContext>();
    db.Database.EnsureCreated();

    try { db.Database.ExecuteSqlRaw("ALTER TABLE WhitelistEntries ADD COLUMN DeviceName TEXT NULL;"); } catch { }
    try { db.Database.ExecuteSqlRaw("ALTER TABLE WhitelistEntries ADD COLUMN Category TEXT NULL;"); } catch { }
}

host.Run();
