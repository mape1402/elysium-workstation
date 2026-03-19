using Elysium.WorkStation.Models;

namespace Elysium.WorkStation.Services
{
    public interface IFileRepository
    {
        Task SaveAsync(FileEntry entry);
        Task<List<FileEntry>> GetRecentAsync(int count = 100);
        Task<List<FileEntry>> DeleteOlderThanAsync(DateTime cutoff);
    }
}
