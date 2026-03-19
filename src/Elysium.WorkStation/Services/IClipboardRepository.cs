using Elysium.WorkStation.Models;

namespace Elysium.WorkStation.Services
{
    public interface IClipboardRepository
    {
        Task SaveAsync(ClipboardEntry entry);
        Task<List<ClipboardEntry>> GetRecentAsync(int count = 100);
        Task<List<ClipboardEntry>> DeleteOlderThanAsync(DateTime cutoff);
    }
}
