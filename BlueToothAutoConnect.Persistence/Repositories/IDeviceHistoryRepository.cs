using BlueToothAutoConnect.Persistence.Entities;

namespace BlueToothAutoConnect.Persistence.Repositories;

public interface IDeviceHistoryRepository
{
    Task<DeviceHistory?> GetByDeviceIdAsync(string deviceId);
    Task<List<DeviceHistory>> GetByDeviceAddressAsync(string deviceAddress);
    Task<DeviceHistory> AddAsync(DeviceHistory entry);
    Task<DeviceHistory?> UpdateAsync(DeviceHistory entry);
    Task<List<DeviceHistory>> GetReplacedDevicesAsync(string deviceAddress);
}