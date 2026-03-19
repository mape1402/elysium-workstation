using Elysium.WorkStation.Models;

namespace Elysium.WorkStation.Services
{
    public interface INotificationRepository
    {
        Task SaveAsync(NotificationEntry entry);
        Task<List<NotificationEntry>> GetAllAsync();
        Task DeleteAllAsync();
        Task<int> DeleteOlderThanAsync(DateTime cutoff);
    }
}
