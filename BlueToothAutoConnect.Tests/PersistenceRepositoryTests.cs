using BlueToothAutoConnect.Persistence.Data;
using BlueToothAutoConnect.Persistence.Entities;
using BlueToothAutoConnect.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Xunit;

namespace BlueToothAutoConnect.Tests;

public class PersistenceRepositoryTests : IDisposable
{
    private readonly SqliteConnection _keepAliveConnection;
    private readonly WhitelistRepository _whitelistRepo;
    private readonly DeviceHistoryRepository _historyRepo;

    public PersistenceRepositoryTests()
    {
        var connectionString = $"Data Source={Guid.NewGuid()};Mode=Memory;Cache=Shared";
        _keepAliveConnection = new SqliteConnection(connectionString);
        _keepAliveConnection.Open();

        var options = new DbContextOptionsBuilder<BluetoothDbContext>()
            .UseSqlite(connectionString)
            .Options;

        using (var context = new BluetoothDbContext(options))
            context.Database.EnsureCreated();

        var contextFactory = new TestDbContextFactory(options);
        _whitelistRepo = new WhitelistRepository(contextFactory);
        _historyRepo = new DeviceHistoryRepository(contextFactory);
    }

    [Fact]
    public async Task WhitelistRepository_AddAndGetByAddress_ReturnsEntry()
    {
        var entry = new WhitelistEntry
        {
            DeviceAddress = "11:22:33:44:55:66",
            FriendlyName = "Test Headphones",
            IsActive = true,
            AddedAt = DateTime.UtcNow
        };

        await _whitelistRepo.AddAsync(entry);

        var fetched = await _whitelistRepo.GetByDeviceAddressAsync("11:22:33:44:55:66");

        Assert.NotNull(fetched);
        Assert.Equal("Test Headphones", fetched.FriendlyName);
        Assert.True(fetched.IsActive);
    }

    [Fact]
    public async Task WhitelistRepository_GetAllActive_FiltersInactive()
    {
        await _whitelistRepo.AddAsync(new WhitelistEntry { DeviceAddress = "11:11:11:11:11:11", IsActive = true });
        await _whitelistRepo.AddAsync(new WhitelistEntry { DeviceAddress = "22:22:22:22:22:22", IsActive = false });

        var activeEntries = await _whitelistRepo.GetAllActiveAsync();

        Assert.Single(activeEntries);
        Assert.Equal("11:11:11:11:11:11", activeEntries[0].DeviceAddress);
    }

    [Fact]
    public async Task WhitelistRepository_ParallelReads_UseIndependentContexts()
    {
        await _whitelistRepo.AddAsync(new WhitelistEntry
        {
            DeviceAddress = "11:22:33:44:55:66",
            FriendlyName = "Test Headphones",
            IsActive = true
        });

        var reads = Enumerable.Range(0, 12).Select(async _ =>
        {
            var entries = await _whitelistRepo.GetAllActiveAsync();
            var match = await _whitelistRepo.FindMatchingEntryAsync(
                "11:22:33:44:55:66",
                "Test Headphones",
                "Audio");

            Assert.Single(entries);
            Assert.NotNull(match);
        });

        await Task.WhenAll(reads);
    }

    [Fact]
    public async Task WhitelistRepository_FindMatchingEntry_NameOnlyRuleIgnoresUnselectedCriteria()
    {
        await _whitelistRepo.AddAsync(new WhitelistEntry
        {
            DeviceAddress = string.Empty,
            DeviceName = "BT Mouse",
            Category = null,
            FriendlyName = "Mouse",
            IsActive = true
        });

        var match = await _whitelistRepo.FindMatchingEntryAsync(
            "11:22:33:44:55:66",
            "bt mouse",
            "Input.Keyboard");

        Assert.NotNull(match);
        Assert.Equal("Mouse", match.FriendlyName);
    }

    [Fact]
    public async Task WhitelistRepository_FindMatchingEntry_AllPopulatedCriteriaMustMatch()
    {
        await _whitelistRepo.AddAsync(new WhitelistEntry
        {
            DeviceAddress = "11:22:33:44:55:66",
            DeviceName = "BT Mouse",
            Category = "Input.Mouse",
            FriendlyName = "Mouse",
            IsActive = true
        });

        var mismatch = await _whitelistRepo.FindMatchingEntryAsync(
            "11:22:33:44:55:66",
            "BT Mouse",
            "Input.Keyboard");
        var match = await _whitelistRepo.FindMatchingEntryAsync(
            "11:22:33:44:55:66",
            "BT Mouse",
            "Input.Mouse");

        Assert.Null(mismatch);
        Assert.NotNull(match);
    }

    [Fact]
    public async Task WhitelistRepository_FindMatchingEntry_RuleWithoutCriteriaNeverMatches()
    {
        await _whitelistRepo.AddAsync(new WhitelistEntry
        {
            DeviceAddress = string.Empty,
            DeviceName = null,
            Category = null,
            FriendlyName = "Empty rule",
            IsActive = true
        });

        var match = await _whitelistRepo.FindMatchingEntryAsync(
            "11:22:33:44:55:66",
            "BT Mouse",
            "Input.Mouse");

        Assert.Null(match);
    }

    [Fact]
    public async Task DeviceHistoryRepository_AddAndUpdate_PersistsChanges()
    {
        var history = new DeviceHistory
        {
            DeviceAddress = "AA:BB:CC:11:22:33",
            DeviceId = "Device1",
            LastConnectedAt = DateTime.UtcNow
        };

        await _historyRepo.AddAsync(history);

        history.ReplacedByDeviceId = "Device2";
        history.ReplacedAt = DateTime.UtcNow;
        await _historyRepo.UpdateAsync(history);

        var records = await _historyRepo.GetByDeviceAddressAsync("AA:BB:CC:11:22:33");

        Assert.Single(records);
        Assert.Equal("Device2", records[0].ReplacedByDeviceId);
    }

    public void Dispose()
    {
        _keepAliveConnection.Dispose();
    }

    private sealed class TestDbContextFactory(DbContextOptions<BluetoothDbContext> options)
        : IDbContextFactory<BluetoothDbContext>
    {
        public BluetoothDbContext CreateDbContext() => new(options);

        public Task<BluetoothDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(CreateDbContext());
        }
    }
}
