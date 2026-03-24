using Elysium.WorkStation.Models;

namespace Elysium.WorkStation.Services
{
    public interface INoteRepository
    {
        Task SaveAsync(NoteEntry entry);
        Task UpdateAsync(NoteEntry entry);
        Task<List<NoteEntry>> GetAllAsync();
        Task DeleteAsync(int id);
        Task DeleteAllAsync();
    }
}
