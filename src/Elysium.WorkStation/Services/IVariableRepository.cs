using Elysium.WorkStation.Models;

namespace Elysium.WorkStation.Services
{
    public interface IVariableRepository
    {
        Task<List<VariableGroup>> GetGroupsAsync();
        Task<VariableGroup> SaveGroupAsync(VariableGroup group);
        Task DeleteGroupAsync(int groupId);

        Task<List<WorkVariable>> GetByGroupAsync(int groupId);
        Task<List<WorkVariable>> GetSecretVariablesAsync();
        Task<WorkVariable> SaveVariableAsync(WorkVariable variable);
        Task DeleteVariableAsync(int variableId);
        Task ResetSecretsAsync();
    }
}
