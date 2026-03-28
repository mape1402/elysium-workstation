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

            var filesDir = Path.Combine(FileSystem.AppDataDirectory, "files");
            Directory.CreateDirectory(filesDir);
            var folderSyncUploadsDir = Path.Combine(FileSystem.AppDataDirectory, "folder-sync-uploads");
            Directory.CreateDirectory(folderSyncUploadsDir);

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

            _host.MapPost("/api/folder-sync/upload", async (HttpRequest request) =>
            {
                var form = await request.ReadFormAsync();
                var file = form.Files["file"];
                if (file is null) return Results.BadRequest("No file provided.");

                var syncId = form["syncId"].ToString();
                if (string.IsNullOrWhiteSpace(syncId))
                {
                    return Results.BadRequest("syncId is required.");
                }

                var safeSyncId = MakeSafeFileOrFolderName(syncId);
                var uploadId = Guid.NewGuid().ToString("N");
                var extension = Path.GetExtension(file.FileName);
                if (string.IsNullOrWhiteSpace(extension))
                {
                    extension = ".bin";
                }

                var syncDir = Path.Combine(folderSyncUploadsDir, safeSyncId);
                Directory.CreateDirectory(syncDir);

                var destinationPath = Path.Combine(syncDir, uploadId + extension);
                await using var fs = File.Create(destinationPath);
                await file.CopyToAsync(fs);
                await fs.FlushAsync();

                return Results.Ok(new { uploadId, fileSize = file.Length });
            });

            _host.MapGet("/api/folder-sync/download/{syncId}/{uploadId}", (string syncId, string uploadId) =>
            {
                var safeSyncId = MakeSafeFileOrFolderName(syncId);
                var safeUploadId = MakeSafeFileOrFolderName(uploadId);
                var syncDir = Path.Combine(folderSyncUploadsDir, safeSyncId);
                if (!Directory.Exists(syncDir))
                {
                    return Results.NotFound();
                }

                var filePath = Directory
                    .GetFiles(syncDir, safeUploadId + ".*", SearchOption.TopDirectoryOnly)
                    .FirstOrDefault();
                if (filePath is null)
                {
                    return Results.NotFound();
                }

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

        private static string MakeSafeFileOrFolderName(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                return string.Empty;
            }

            var invalid = Path.GetInvalidFileNameChars();
            var buffer = input.Trim().ToCharArray();
            for (var index = 0; index < buffer.Length; index++)
            {
                if (invalid.Contains(buffer[index]))
                {
                    buffer[index] = '_';
                }
            }

            return new string(buffer);
        }
    }
}
