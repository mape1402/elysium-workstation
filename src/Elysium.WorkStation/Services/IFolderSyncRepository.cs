using Elysium.WorkStation.Models;

namespace Elysium.WorkStation.Services
{
    public interface IFolderSyncRepository
    {
        Task<List<FolderSyncLink>> GetAllAsync();
        Task<FolderSyncLink> GetByIdAsync(int id);
        Task<FolderSyncLink> GetBySyncIdAsync(string syncId);
        Task<FolderSyncLink> SaveAsync(FolderSyncLink link);
        Task DeleteAsync(int id);
    }
}
