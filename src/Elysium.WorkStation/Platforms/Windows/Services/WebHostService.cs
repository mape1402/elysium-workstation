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

            builder.Services.Configure<KestrelServerOptions>(kestrel =>
            {
                kestrel.ListenAnyIP(port);
                kestrel.Limits.MaxRequestBodySize = null;
            });

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

            var filesDir = Path.Combine(Path.GetTempPath(), "ElysiumWorkStation", "files");
            Directory.CreateDirectory(filesDir);

            _host.MapPost("/api/files", async (HttpRequest request) =>
            {
                var form = await request.ReadFormAsync();
                var file = form.Files["file"];
                if (file is null) return Results.BadRequest("No file provided.");

                var fileId  = Guid.NewGuid().ToString("N");
                var fileDir = Path.Combine(filesDir, fileId);
                Directory.CreateDirectory(fileDir);

                var safeName = Path.GetFileName(file.FileName);
                await using var fs = File.Create(Path.Combine(fileDir, safeName));
                await file.CopyToAsync(fs);

                return Results.Ok(new { fileId, fileName = safeName, fileSize = file.Length });
            });

            _host.MapGet("/api/files/{fileId}", (string fileId) =>
            {
                var fileDir  = Path.Combine(filesDir, fileId);
                if (!Directory.Exists(fileDir)) return Results.NotFound();
                var filePath = Directory.GetFiles(fileDir).FirstOrDefault();
                if (filePath is null) return Results.NotFound();
                return Results.File(filePath, "application/octet-stream", Path.GetFileName(filePath));
            });

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
