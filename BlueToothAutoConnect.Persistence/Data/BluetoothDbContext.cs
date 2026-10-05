using BlueToothAutoConnect.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace BlueToothAutoConnect.Persistence.Data;

public class BluetoothDbContext : DbContext
{
    public BluetoothDbContext(DbContextOptions<BluetoothDbContext> options)
        : base(options)
    {
    }

    public DbSet<WhitelistEntry> WhitelistEntries { get; set; }
    public DbSet<DeviceHistory> DeviceHistory { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Configure WhitelistEntry
        modelBuilder.Entity<WhitelistEntry>(entity =>
        {
            entity.HasIndex(e => e.DeviceAddress);
            entity.HasIndex(e => e.DeviceName);
            entity.HasIndex(e => e.Category);
            entity.Property(e => e.DeviceAddress).IsRequired(false);
            entity.Property(e => e.DeviceName).IsRequired(false);
            entity.Property(e => e.Category).IsRequired(false);
            entity.Property(e => e.FriendlyName).IsRequired();
        });

        // Configure DeviceHistory
        modelBuilder.Entity<DeviceHistory>(entity =>
        {
            entity.HasIndex(e => e.DeviceAddress);
            entity.HasIndex(e => e.DeviceId);
            entity.Property(e => e.DeviceAddress).IsRequired();
            entity.Property(e => e.DeviceId).IsRequired();
        });
    }
}