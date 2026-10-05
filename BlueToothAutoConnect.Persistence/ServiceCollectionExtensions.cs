using BlueToothAutoConnect.Persistence.Data;
using BlueToothAutoConnect.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BlueToothAutoConnect.Persistence;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddPersistenceServices(
        this IServiceCollection services,
        string connectionString)
    {
        services.AddDbContextFactory<BluetoothDbContext>(options =>
            options.UseSqlite(connectionString));

        services.AddScoped<IWhitelistRepository, WhitelistRepository>();
        services.AddScoped<IDeviceHistoryRepository, DeviceHistoryRepository>();

        return services;
    }
}