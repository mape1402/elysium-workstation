using Elysium.WorkStation.Data;
using Elysium.WorkStation.Models;
using Microsoft.EntityFrameworkCore;

namespace Elysium.WorkStation.Services
{
    public class FileRepository : IFileRepository
    {
        private readonly IDbContextFactory<AppDbContext> _factory;

        public FileRepository(IDbContextFactory<AppDbContext> factory)
        {
            _factory = factory;
        }

        public async Task SaveAsync(FileEntry entry)
        {
            await using var db = await _factory.CreateDbContextAsync();
            if (await db.FileHistory.AnyAsync(f => f.FileId == entry.FileId)) return;
            db.FileHistory.Add(entry);
            await db.SaveChangesAsync();
        }

        public async Task<List<FileEntry>> GetRecentAsync(int count = 100)
        {
            await using var db = await _factory.CreateDbContextAsync();
            return await db.FileHistory
                .OrderByDescending(e => e.Timestamp)
                .Take(count)
                .ToListAsync();
        }
    }
}
