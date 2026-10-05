using BlueToothAutoConnect.Persistence.Entities;

namespace BlueToothAutoConnect.Persistence.Repositories;

public interface IWhitelistRepository
{
    Task<WhitelistEntry?> GetByIdAsync(int id);
    Task<WhitelistEntry?> GetByDeviceAddressAsync(string deviceAddress);
    Task<WhitelistEntry?> FindMatchingEntryAsync(string address, string name, string category);
    Task<List<WhitelistEntry>> GetAllActiveAsync();
    Task<WhitelistEntry> AddAsync(WhitelistEntry entry);
    Task<WhitelistEntry?> UpdateAsync(WhitelistEntry entry);
    Task<bool> DeleteAsync(int id);
    Task<bool> ExistsByDeviceAddressAsync(string deviceAddress);
}