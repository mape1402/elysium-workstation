using Elysium.WorkStation.Hubs;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;

namespace Elysium.WorkStation.Services
{
    public class WebHostService : IWebHostService
    {
        private readonly ISettingsService _settingsService;
        private WebApplication _host;

        public string BaseUrl { get; private set; } = string.Empty;
        public bool IsRunning => _host is not null;

        public WebHostService(ISettingsService settingsService)
        {
            _settingsService = settingsService;
        }

        public async Task StartAsync()
        {
            if (IsRunning) return;

            int port = _settingsService.ServerPort;

            var builder = WebApplication.CreateBuilder();

            builder.Services.Configure<KestrelServerOptions>(kestrel => kestrel.ListenAnyIP(port));

            builder.Services.AddSignalR();
            builder.Services.AddCors(options =>
                options.AddDefaultPolicy(policy =>
                    policy.AllowAnyOrigin()
                          .AllowAnyMethod()
                          .AllowAnyHeader()));

            _host = builder.Build();

            _host.UseCors();

            _host.MapGet("/api/status", () => Results.Ok(new
            {
                Status  = "Running",
                Version = AppInfo.VersionString,
                Time    = DateTime.UtcNow
            }));

            _host.MapHub<WorkStationHub>("/hubs/workstation");

            BaseUrl = _settingsService.ServerUrl;
            await _host.StartAsync();
        }

        public async Task StopAsync()
        {
            if (_host is null) return;

            await _host.StopAsync();
            await _host.DisposeAsync();
            _host = null;
            BaseUrl = string.Empty;
        }
    }
}
