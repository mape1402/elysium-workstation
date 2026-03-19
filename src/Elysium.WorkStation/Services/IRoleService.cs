using Elysium.WorkStation.Models;

namespace Elysium.WorkStation.Services
{
    public interface IRoleService
    {
        AppRole CurrentRole { get; }
        event EventHandler<AppRole> RoleChanged;
        Task<bool> IsServerRunningAsync();
        Task ActivateServerAsync();
        void SetClientRole();
    }
}
