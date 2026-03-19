using Elysium.WorkStation.Models;

namespace Elysium.WorkStation.Services
{
    public class RoleService : IRoleService
    {
        private readonly IWebHostService _webHostService;
        private AppRole _currentRole = AppRole.Undetermined;

        public AppRole CurrentRole => _currentRole;
        public event EventHandler<AppRole> RoleChanged;

        public RoleService(IWebHostService webHostService)
        {
            _webHostService = webHostService;
        }

        public async Task<bool> IsServerRunningAsync()
        {
            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromMilliseconds(500) };
                var response = await client.GetAsync("http://localhost:5050/api/status");
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        public async Task ActivateServerAsync()
        {
            await _webHostService.StartAsync();
            _currentRole = AppRole.Server;
            RoleChanged?.Invoke(this, _currentRole);
        }

        public void SetClientRole()
        {
            _currentRole = AppRole.Client;
            RoleChanged?.Invoke(this, _currentRole);
        }
    }
}
