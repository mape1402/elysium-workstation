using Elysium.WorkStation.Models;
using Microsoft.AspNetCore.SignalR.Client;
using System.Collections.ObjectModel;

namespace Elysium.WorkStation.Services
{
    public class ClipboardSyncService : IClipboardSyncService, IAsyncDisposable
    {
        private HubConnection _connection;

        public ObservableCollection<ClipboardEntry> History { get; } = new();

        public bool IsConnected => _connection?.State == HubConnectionState.Connected;

        public event EventHandler ConnectionStateChanged;

        public async Task StartAsync(string hubUrl)
        {
            if (_connection is not null) return;

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
                MainThread.BeginInvokeOnMainThread(() => History.Insert(0, entry));
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
                Text = text,
                SenderName = Environment.MachineName,
                Timestamp = DateTime.Now,
                IsFromSelf = true
            };
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
