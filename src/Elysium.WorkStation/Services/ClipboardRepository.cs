using Elysium.WorkStation.Data;
using Elysium.WorkStation.Models;
using Microsoft.EntityFrameworkCore;

namespace Elysium.WorkStation.Services
{
    public class ClipboardRepository : IClipboardRepository
    {
        private readonly IDbContextFactory<AppDbContext> _factory;

        public ClipboardRepository(IDbContextFactory<AppDbContext> factory)
        {
            _factory = factory;
        }

        public async Task SaveAsync(ClipboardEntry entry)
        {
            await using var db = await _factory.CreateDbContextAsync();
            db.ClipboardHistory.Add(entry);
            await db.SaveChangesAsync();
        }

        public async Task<List<ClipboardEntry>> GetRecentAsync(int count = 100)
        {
            await using var db = await _factory.CreateDbContextAsync();
            return await db.ClipboardHistory
                .OrderByDescending(e => e.Timestamp)
                .Take(count)
                .ToListAsync();
        }

        public async Task<List<ClipboardEntry>> DeleteOlderThanAsync(DateTime cutoff)
        {
            await using var db = await _factory.CreateDbContextAsync();
            var old = await db.ClipboardHistory
                .Where(e => e.Timestamp < cutoff)
                .ToListAsync();

            if (old.Count > 0)
            {
                db.ClipboardHistory.RemoveRange(old);
                await db.SaveChangesAsync();
            }

            return old;
        }
    }
}
