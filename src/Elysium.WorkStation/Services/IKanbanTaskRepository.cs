using Elysium.WorkStation.Models;

namespace Elysium.WorkStation.Services
{
    public interface IKanbanTaskRepository
    {
        Task<List<KanbanTask>> GetAllAsync();
        Task<List<KanbanTask>> GetByStatusAsync(KanbanStatus status);
        Task SaveAsync(KanbanTask task);
        Task UpdateAsync(KanbanTask task);
        Task DeleteAsync(int id);
        Task HideCompletedOlderThanAsync(DateTime cutoff);
    }
}
