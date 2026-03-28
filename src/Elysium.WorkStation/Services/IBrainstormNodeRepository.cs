using Elysium.WorkStation.Models;

namespace Elysium.WorkStation.Services
{
    public interface IBrainstormNodeRepository
    {
        Task<List<BrainstormNode>> GetChildrenAsync(int? parentId);
        Task<List<BrainstormNode>> GetPathAsync(int? nodeId);
        Task<BrainstormNode> GetByIdAsync(int id);
        Task SaveAsync(BrainstormNode node);
        Task UpdateAsync(BrainstormNode node);
        Task DeleteBranchAsync(int id);
    }
}
