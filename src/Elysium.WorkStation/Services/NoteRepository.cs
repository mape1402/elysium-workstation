using Elysium.WorkStation.Data;
using Elysium.WorkStation.Models;
using Microsoft.EntityFrameworkCore;

namespace Elysium.WorkStation.Services
{
    public class NoteRepository : INoteRepository
    {
        private readonly IDbContextFactory<AppDbContext> _factory;

        public NoteRepository(IDbContextFactory<AppDbContext> factory)
        {
            _factory = factory;
        }

        public async Task SaveAsync(NoteEntry entry)
        {
            await using var db = await _factory.CreateDbContextAsync();
            db.Notes.Add(entry);
            await db.SaveChangesAsync();
        }

        public async Task UpdateAsync(NoteEntry entry)
        {
            await using var db = await _factory.CreateDbContextAsync();
            db.Notes.Update(entry);
            await db.SaveChangesAsync();
        }

        public async Task<List<NoteEntry>> GetAllAsync()
        {
            await using var db = await _factory.CreateDbContextAsync();
            return await db.Notes
                .OrderByDescending(n => n.Timestamp)
                .ToListAsync();
        }

        public async Task DeleteAsync(int id)
        {
            await using var db = await _factory.CreateDbContextAsync();
            await db.Notes
                .Where(n => n.Id == id)
                .ExecuteDeleteAsync();
        }

        public async Task DeleteAllAsync()
        {
            await using var db = await _factory.CreateDbContextAsync();
            await db.Notes.ExecuteDeleteAsync();
        }
    }
}
