using Elysium.WorkStation.Models;

namespace Elysium.WorkStation.Services
{
    public class DefaultRoleService : IRoleService
    {
        public AppRole CurrentRole { get; private set; } = AppRole.Client;

        public event EventHandler<AppRole> RoleChanged;

        public Task<bool> IsServerRunningAsync() => Task.FromResult(false);

        public Task ActivateServerAsync() => Task.CompletedTask;

        public void SetClientRole() { }
    }
}
