using Elysium.WorkStation.Data;
using Elysium.WorkStation.Models;
using Microsoft.EntityFrameworkCore;

namespace Elysium.WorkStation.Services
{
    public class VariableRepository : IVariableRepository
    {
        private readonly IDbContextFactory<AppDbContext> _factory;

        public VariableRepository(IDbContextFactory<AppDbContext> factory)
        {
            _factory = factory;
        }

        public async Task<List<VariableGroup>> GetGroupsAsync()
        {
            await using var db = await _factory.CreateDbContextAsync();
            return await db.VariableGroups
                .OrderBy(g => g.Name)
                .ToListAsync();
        }

        public async Task<VariableGroup> SaveGroupAsync(VariableGroup group)
        {
            await using var db = await _factory.CreateDbContextAsync();

            if (group.Id == 0)
            {
                group.CreatedAt = DateTime.Now;
                db.VariableGroups.Add(group);
            }
            else
            {
                db.VariableGroups.Update(group);
            }

            await db.SaveChangesAsync();
            return group;
        }

        public async Task DeleteGroupAsync(int groupId)
        {
            await using var db = await _factory.CreateDbContextAsync();
            await db.WorkVariables
                .Where(v => v.GroupId == groupId)
                .ExecuteDeleteAsync();
            await db.VariableGroups
                .Where(g => g.Id == groupId)
                .ExecuteDeleteAsync();
        }

        public async Task<List<WorkVariable>> GetByGroupAsync(int groupId)
        {
            await using var db = await _factory.CreateDbContextAsync();
            return await db.WorkVariables
                .Where(v => v.GroupId == groupId)
                .OrderBy(v => v.VariableKey)
                .ToListAsync();
        }

        public async Task<WorkVariable> SaveVariableAsync(WorkVariable variable)
        {
            await using var db = await _factory.CreateDbContextAsync();

            var duplicate = await db.WorkVariables
                .Where(v => v.GroupId == variable.GroupId &&
                            v.VariableKey == variable.VariableKey &&
                            v.Id != variable.Id)
                .AnyAsync();

            if (duplicate)
                throw new InvalidOperationException("Ya existe una variable con esa clave en este grupo.");

            variable.UpdatedAt = DateTime.Now;
            if (variable.Id == 0)
                db.WorkVariables.Add(variable);
            else
                db.WorkVariables.Update(variable);

            await db.SaveChangesAsync();
            return variable;
        }

        public async Task DeleteVariableAsync(int variableId)
        {
            await using var db = await _factory.CreateDbContextAsync();
            await db.WorkVariables
                .Where(v => v.Id == variableId)
                .ExecuteDeleteAsync();
        }
    }
}
