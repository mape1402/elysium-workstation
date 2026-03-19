using System.Collections.ObjectModel;
using System.Net.Http.Json;
using System.Text.Json;
using Elysium.WorkStation.Models;
using Microsoft.AspNetCore.SignalR.Client;

namespace Elysium.WorkStation.Services
{
    public class FileTransferService : IFileTransferService
    {
        private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

        private HubConnection _connection;
        private string _baseUrl = string.Empty;
        private readonly INotificationService _notificationService;

        public ObservableCollection<FileEntry> History { get; } = [];

        public bool IsConnected => _connection?.State == HubConnectionState.Connected;
        public event EventHandler ConnectionStateChanged;

        public FileTransferService(INotificationService notificationService)
        {
            _notificationService = notificationService;
        }

        public async Task StartAsync(string hubUrl)
        {
            if (_connection is not null) return;

            _baseUrl = hubUrl[..hubUrl.LastIndexOf("/hubs/", StringComparison.Ordinal)];

            _connection = new HubConnectionBuilder()
                .WithUrl(hubUrl)
                .WithAutomaticReconnect()
                .Build();

            _connection.On<string, string, long, string>("ReceiveFileAnnouncement",
                (fileId, fileName, fileSize, senderName) =>
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        History.Insert(0, new FileEntry
                        {
                            FileId     = fileId,
                            FileName   = fileName,
                            FileSize   = fileSize,
                            SenderName = senderName,
                            IsFromSelf = false,
                            Timestamp  = DateTime.Now
                        });
                        _notificationService.Notify("📂 Archivo recibido", $"{senderName} envió «{fileName}»");
                    }));

            _connection.Closed      += _ => { ConnectionStateChanged?.Invoke(this, EventArgs.Empty); return Task.CompletedTask; };
            _connection.Reconnected += _ => { ConnectionStateChanged?.Invoke(this, EventArgs.Empty); return Task.CompletedTask; };

            for (int i = 0; i < 10; i++)
            {
                try
                {
                    await _connection.StartAsync();
                    ConnectionStateChanged?.Invoke(this, EventArgs.Empty);
                    return;
                }
                catch { await Task.Delay(2000); }
            }
        }

        public async Task SendFilesAsync(IEnumerable<string> filePaths)
        {
            if (_connection?.State != HubConnectionState.Connected) return;

            string senderName = DeviceInfo.Name;

            foreach (var path in filePaths)
            {
                if (!File.Exists(path)) continue;

                var info = new FileInfo(path);
                using var content = new MultipartFormDataContent();
                await using var stream = File.OpenRead(path);
                content.Add(new StreamContent(stream), "file", info.Name);

                using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
                var response = await client.PostAsync($"{_baseUrl}/api/files", content);
                if (!response.IsSuccessStatusCode) continue;

                var result = await response.Content.ReadFromJsonAsync<FileUploadResult>(JsonOptions);
                if (result is null) continue;

                await _connection.InvokeAsync("AnnounceFile", result.FileId, info.Name, info.Length, senderName);

                MainThread.BeginInvokeOnMainThread(() =>
                    History.Insert(0, new FileEntry
                    {
                        FileId     = result.FileId,
                        FileName   = info.Name,
                        FileSize   = info.Length,
                        SenderName = senderName,
                        IsFromSelf = true,
                        Timestamp  = DateTime.Now
                    }));
            }
        }

        public async Task DownloadFileAsync(FileEntry entry, string destinationPath)
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
            using var response = await client.GetAsync(
                $"{_baseUrl}/api/files/{entry.FileId}",
                HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            await using var responseStream = await response.Content.ReadAsStreamAsync();
            await using var fileStream = File.Create(destinationPath);
            await responseStream.CopyToAsync(fileStream);
            await fileStream.FlushAsync();
        }

        public async Task StopAsync()
        {
            if (_connection is null) return;
            await _connection.StopAsync();
            await _connection.DisposeAsync();
            _connection = null;
        }

        private record FileUploadResult(string FileId, string FileName, long FileSize);
    }
}
