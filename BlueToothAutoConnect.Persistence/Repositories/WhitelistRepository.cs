using BlueToothAutoConnect.Persistence.Data;
using BlueToothAutoConnect.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace BlueToothAutoConnect.Persistence.Repositories;

public class WhitelistRepository : IWhitelistRepository
{
    private readonly IDbContextFactory<BluetoothDbContext> _contextFactory;

    public WhitelistRepository(IDbContextFactory<BluetoothDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task<WhitelistEntry?> GetByIdAsync(int id)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.WhitelistEntries.AsNoTracking().FirstOrDefaultAsync(e => e.Id == id);
    }

    public async Task<WhitelistEntry?> GetByDeviceAddressAsync(string deviceAddress)
    {
        var normalized = deviceAddress?.Trim().ToLowerInvariant();
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.WhitelistEntries
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.DeviceAddress != null && e.DeviceAddress.ToLower() == normalized);
    }

    public async Task<WhitelistEntry?> FindMatchingEntryAsync(string address, string name, string category)
    {
        var normAddr = (address ?? string.Empty).Trim().ToLowerInvariant();
        var normName = (name ?? string.Empty).Trim().ToLowerInvariant();
        var normCat = (category ?? string.Empty).Trim().ToLowerInvariant();

        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.WhitelistEntries
            .AsNoTracking()
            .Where(e => e.IsActive &&
                ((e.DeviceAddress != null && e.DeviceAddress.Trim() != "") ||
                 (e.DeviceName != null && e.DeviceName.Trim() != "") ||
                 (e.Category != null && e.Category.Trim() != "")))
            .Where(e =>
                (e.DeviceAddress == null || e.DeviceAddress.Trim() == "" ||
                 (!string.IsNullOrEmpty(normAddr) && e.DeviceAddress.Trim().ToLower() == normAddr)) &&
                (e.DeviceName == null || e.DeviceName.Trim() == "" ||
                 (!string.IsNullOrEmpty(normName) && e.DeviceName.Trim().ToLower() == normName)) &&
                (e.Category == null || e.Category.Trim() == "" ||
                 (!string.IsNullOrEmpty(normCat) && e.Category.Trim().ToLower() == normCat)))
            .FirstOrDefaultAsync();
    }

    public async Task<List<WhitelistEntry>> GetAllActiveAsync()
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.WhitelistEntries
            .AsNoTracking()
            .Where(e => e.IsActive)
            .OrderBy(e => e.FriendlyName)
            .ToListAsync();
    }

    public async Task<WhitelistEntry> AddAsync(WhitelistEntry entry)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        context.WhitelistEntries.Add(entry);
        await context.SaveChangesAsync();
        return entry;
    }

    public async Task<WhitelistEntry?> UpdateAsync(WhitelistEntry entry)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var existing = await context.WhitelistEntries.FindAsync(entry.Id);
        if (existing == null)
            return null;

        context.Entry(existing).CurrentValues.SetValues(entry);
        await context.SaveChangesAsync();
        return existing;
    }

    public async Task<bool> DeleteAsync(int id)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var entry = await context.WhitelistEntries.FindAsync(id);
        if (entry == null)
            return false;

        context.WhitelistEntries.Remove(entry);
        await context.SaveChangesAsync();
        return true;
    }

    public async Task<bool> ExistsByDeviceAddressAsync(string deviceAddress)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.WhitelistEntries
            .AnyAsync(e => e.DeviceAddress == deviceAddress);
    }
}