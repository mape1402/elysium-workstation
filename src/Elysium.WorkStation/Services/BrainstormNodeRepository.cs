using Elysium.WorkStation.Data;
using Elysium.WorkStation.Models;
using Microsoft.EntityFrameworkCore;

namespace Elysium.WorkStation.Services
{
    public class BrainstormNodeRepository : IBrainstormNodeRepository
    {
        private readonly IDbContextFactory<AppDbContext> _factory;

        public BrainstormNodeRepository(IDbContextFactory<AppDbContext> factory)
        {
            _factory = factory;
        }

        public async Task<List<BrainstormNode>> GetChildrenAsync(int? parentId)
        {
            await using var db = await _factory.CreateDbContextAsync();
            return await db.BrainstormNodes
                .Where(n => n.ParentId == parentId)
                .OrderByDescending(n => n.UpdatedAt)
                .ThenBy(n => n.Title)
                .ToListAsync();
        }

        public async Task<List<BrainstormNode>> GetPathAsync(int? nodeId)
        {
            if (nodeId is null) return [];

            await using var db = await _factory.CreateDbContextAsync();
            List<BrainstormNode> path = [];
            int? currentId = nodeId;
            int safety = 0;

            while (currentId is int id && safety++ < 200)
            {
                var node = await db.BrainstormNodes
                    .AsNoTracking()
                    .FirstOrDefaultAsync(n => n.Id == id);

                if (node is null) break;

                path.Add(node);
                currentId = node.ParentId;
            }

            path.Reverse();
            return path;
        }

        public async Task<BrainstormNode> GetByIdAsync(int id)
        {
            await using var db = await _factory.CreateDbContextAsync();
            return await db.BrainstormNodes
                .AsNoTracking()
                .FirstOrDefaultAsync(n => n.Id == id);
        }

        public async Task SaveAsync(BrainstormNode node)
        {
            node.CreatedAt = DateTime.Now;
            node.UpdatedAt = node.CreatedAt;

            await using var db = await _factory.CreateDbContextAsync();
            db.BrainstormNodes.Add(node);
            await db.SaveChangesAsync();
        }

        public async Task UpdateAsync(BrainstormNode node)
        {
            await using var db = await _factory.CreateDbContextAsync();
            var existing = await db.BrainstormNodes.FirstOrDefaultAsync(n => n.Id == node.Id);
            if (existing is null) return;

            existing.Title = node.Title;
            existing.Description = node.Description;
            existing.UpdatedAt = DateTime.Now;
            await db.SaveChangesAsync();
        }

        public async Task DeleteBranchAsync(int id)
        {
            await using var db = await _factory.CreateDbContextAsync();
            var allNodes = await db.BrainstormNodes
                .AsNoTracking()
                .Select(n => new { n.Id, n.ParentId })
                .ToListAsync();

            HashSet<int> idsToDelete = [id];
            bool expanded;

            do
            {
                expanded = false;
                foreach (var node in allNodes)
                {
                    if (node.ParentId is int parentId && idsToDelete.Contains(parentId) && idsToDelete.Add(node.Id))
                    {
                        expanded = true;
                    }
                }
            } while (expanded);

            await db.BrainstormNodes
                .Where(n => idsToDelete.Contains(n.Id))
                .ExecuteDeleteAsync();
        }
    }
}
