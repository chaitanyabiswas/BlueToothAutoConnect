using BlueToothAutoConnect.Persistence.Data;
using BlueToothAutoConnect.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace BlueToothAutoConnect.Persistence.Repositories;

public class DeviceHistoryRepository : IDeviceHistoryRepository
{
    private readonly IDbContextFactory<BluetoothDbContext> _contextFactory;

    public DeviceHistoryRepository(IDbContextFactory<BluetoothDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task<DeviceHistory?> GetByDeviceIdAsync(string deviceId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.DeviceHistory
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.DeviceId == deviceId);
    }

    public async Task<List<DeviceHistory>> GetByDeviceAddressAsync(string deviceAddress)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.DeviceHistory
            .AsNoTracking()
            .Where(e => e.DeviceAddress == deviceAddress)
            .OrderByDescending(e => e.LastSeen)
            .ToListAsync();
    }

    public async Task<DeviceHistory> AddAsync(DeviceHistory entry)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        context.DeviceHistory.Add(entry);
        await context.SaveChangesAsync();
        return entry;
    }

    public async Task<DeviceHistory?> UpdateAsync(DeviceHistory entry)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var existing = await context.DeviceHistory.FindAsync(entry.Id);
        if (existing == null)
            return null;

        context.Entry(existing).CurrentValues.SetValues(entry);
        await context.SaveChangesAsync();
        return existing;
    }

    public async Task<List<DeviceHistory>> GetReplacedDevicesAsync(string deviceAddress)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.DeviceHistory
            .AsNoTracking()
            .Where(e => e.DeviceAddress == deviceAddress && e.ReplacedByDeviceId != null)
            .OrderByDescending(e => e.ReplacedAt)
            .ToListAsync();
    }
}