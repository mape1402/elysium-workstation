using Elysium.WorkStation.Models;
using Microsoft.AspNetCore.SignalR.Client;
using System.Collections.ObjectModel;

namespace Elysium.WorkStation.Services
{
    public class ClipboardSyncService : IClipboardSyncService, IAsyncDisposable
    {
        private HubConnection _connection;
        private readonly INotificationService _notificationService;
        private readonly INotificationRepository _notificationRepository;
        private readonly IClipboardRepository _clipboardRepository;

        public ObservableCollection<ClipboardEntry> History { get; } = new();

        public bool IsConnected => _connection?.State == HubConnectionState.Connected;

        public event EventHandler ConnectionStateChanged;

        public ClipboardSyncService(
            INotificationService notificationService,
            INotificationRepository notificationRepository,
            IClipboardRepository clipboardRepository)
        {
            _notificationService        = notificationService;
            _notificationRepository     = notificationRepository;
            _clipboardRepository        = clipboardRepository;
        }

        public async Task StartAsync(string hubUrl)
        {
            if (_connection is not null) return;

            var history = await _clipboardRepository.GetRecentAsync();
            MainThread.BeginInvokeOnMainThread(() =>
            {
                foreach (var item in history)
                    History.Add(item);
            });

            _connection = new HubConnectionBuilder()
                .WithUrl(hubUrl)
                .WithAutomaticReconnect()
                .Build();

            _connection.On<string, string>("ReceiveClipboard", (text, sender) =>
            {
                var entry = new ClipboardEntry
                {
                    Text = text,
                    SenderName = sender,
                    Timestamp = DateTime.Now,
                    IsFromSelf = false
                };

                string preview = text.Length > 60 ? text[..60] + "…" : text;
                const string title = "📋 Portapapeles recibido";
                string message = $"{sender}: {preview}";

                _ = _notificationRepository.SaveAsync(new NotificationEntry
                {
                    Title = title,
                    Message = message,
                    Timestamp = DateTime.Now
                });
                _ = _clipboardRepository.SaveAsync(entry);

                MainThread.BeginInvokeOnMainThread(() =>
                {
                    History.Insert(0, entry);
                    _notificationService.Notify(title, message);
                });
            });

            _connection.Reconnected  += _ => { ConnectionStateChanged?.Invoke(this, EventArgs.Empty); return Task.CompletedTask; };
            _connection.Reconnecting += _ => { ConnectionStateChanged?.Invoke(this, EventArgs.Empty); return Task.CompletedTask; };
            _connection.Closed       += _ => { ConnectionStateChanged?.Invoke(this, EventArgs.Empty); return Task.CompletedTask; };

            for (int attempt = 0; attempt < 10; attempt++)
            {
                try
                {
                    await _connection.StartAsync();
                    ConnectionStateChanged?.Invoke(this, EventArgs.Empty);
                    break;
                }
                catch
                {
                    if (attempt == 9) break;
                    await Task.Delay(1000);
                }
            }
        }

        public async Task SendCurrentClipboardAsync()
        {
            string text = await MainThread.InvokeOnMainThreadAsync(
                () => Clipboard.Default.GetTextAsync());

            if (string.IsNullOrWhiteSpace(text)) return;

            var entry = new ClipboardEntry
            {
                Text       = text,
                SenderName = Environment.MachineName,
                Timestamp  = DateTime.Now,
                IsFromSelf = true
            };
            _ = _clipboardRepository.SaveAsync(entry);
            MainThread.BeginInvokeOnMainThread(() => History.Insert(0, entry));

            if (_connection?.State == HubConnectionState.Connected)
            {
                try { await _connection.InvokeAsync("ClipboardSync", text, Environment.MachineName); }
                catch { }
            }
        }

        public async Task StopAsync()
        {
            if (_connection is not null)
            {
                await _connection.StopAsync();
                await _connection.DisposeAsync();
                _connection = null;
            }
        }

        public async ValueTask DisposeAsync() => await StopAsync();
    }
}
