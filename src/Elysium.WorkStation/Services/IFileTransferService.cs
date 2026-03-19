using System.Collections.ObjectModel;
using Elysium.WorkStation.Models;

namespace Elysium.WorkStation.Services
{
    public interface IFileTransferService
    {
        ObservableCollection<FileEntry> History { get; }
        bool IsConnected { get; }
        event EventHandler ConnectionStateChanged;
        Task StartAsync(string hubUrl);
        Task SendFilesAsync(IEnumerable<string> filePaths);
        Task DownloadFileAsync(FileEntry entry, string destinationPath);
        Task StopAsync();
    }
}
