using Elysium.WorkStation.Data;
using Elysium.WorkStation.Models;
using Microsoft.EntityFrameworkCore;

namespace Elysium.WorkStation.Services
{
    public class NotificationRepository : INotificationRepository
    {
        private readonly IDbContextFactory<AppDbContext> _factory;

        public NotificationRepository(IDbContextFactory<AppDbContext> factory)
        {
            _factory = factory;
        }

        public async Task SaveAsync(NotificationEntry entry)
        {
            await using var db = await _factory.CreateDbContextAsync();
            db.Notifications.Add(entry);
            await db.SaveChangesAsync();
        }

        public async Task<List<NotificationEntry>> GetAllAsync()
        {
            await using var db = await _factory.CreateDbContextAsync();
            return await db.Notifications
                .OrderByDescending(n => n.Timestamp)
                .ToListAsync();
        }

        public async Task DeleteAllAsync()
        {
            await using var db = await _factory.CreateDbContextAsync();
            await db.Notifications.ExecuteDeleteAsync();
        }

        public async Task<int> DeleteOlderThanAsync(DateTime cutoff)
        {
            await using var db = await _factory.CreateDbContextAsync();
            return await db.Notifications
                .Where(n => n.Timestamp < cutoff)
                .ExecuteDeleteAsync();
        }
    }
}
