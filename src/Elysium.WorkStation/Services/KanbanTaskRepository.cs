using Elysium.WorkStation.Data;
using Elysium.WorkStation.Models;
using Microsoft.EntityFrameworkCore;

namespace Elysium.WorkStation.Services
{
    public class KanbanTaskRepository : IKanbanTaskRepository
    {
        private readonly IDbContextFactory<AppDbContext> _factory;

        public KanbanTaskRepository(IDbContextFactory<AppDbContext> factory)
        {
            _factory = factory;
        }

        public async Task<List<KanbanTask>> GetAllAsync()
        {
            await using var db = await _factory.CreateDbContextAsync();
            return await db.KanbanTasks
                .OrderBy(t => t.SortOrder)
                .ThenByDescending(t => t.CreatedAt)
                .ToListAsync();
        }

        public async Task<List<KanbanTask>> GetByStatusAsync(KanbanStatus status)
        {
            await using var db = await _factory.CreateDbContextAsync();
            return await db.KanbanTasks
                .Where(t => t.Status == status)
                .OrderBy(t => t.SortOrder)
                .ThenByDescending(t => t.CreatedAt)
                .ToListAsync();
        }

        public async Task SaveAsync(KanbanTask task)
        {
            await using var db = await _factory.CreateDbContextAsync();
            db.KanbanTasks.Add(task);
            await db.SaveChangesAsync();
        }

        public async Task UpdateAsync(KanbanTask task)
        {
            await using var db = await _factory.CreateDbContextAsync();
            db.KanbanTasks.Update(task);
            await db.SaveChangesAsync();
        }

        public async Task DeleteAsync(int id)
        {
            await using var db = await _factory.CreateDbContextAsync();
            await db.KanbanTasks
                .Where(t => t.Id == id)
                .ExecuteDeleteAsync();
        }
    }
}
