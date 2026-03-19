using Elysium.WorkStation.Models;
using System.Collections.ObjectModel;

namespace Elysium.WorkStation.Services
{
    public interface IClipboardSyncService
    {
        ObservableCollection<ClipboardEntry> History { get; }
        bool IsConnected { get; }
        event EventHandler ConnectionStateChanged;
        Task StartAsync(string hubUrl);
        Task SendCurrentClipboardAsync();
        Task StopAsync();
    }
}
