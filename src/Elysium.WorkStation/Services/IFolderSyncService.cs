using Elysium.WorkStation.Models;
using System.Collections.ObjectModel;

namespace Elysium.WorkStation.Services
{
    public interface IFolderSyncService
    {
        ObservableCollection<FolderSyncLink> Links { get; }
        ObservableCollection<FolderSyncInvite> PendingInvites { get; }
        bool IsConnected { get; }

        event EventHandler StateChanged;

        Task StartAsync(string hubUrl);
        Task StopAsync();
        Task ReloadAsync();

        Task<FolderSyncLink> CreateSyncRequestAsync(
            string name,
            string description,
            string localFolderPath,
            IEnumerable<string> ignorePaths);

        Task<FolderSyncLink> AcceptInviteAsync(FolderSyncInvite invite, string localFolderPath);
        Task RejectInviteAsync(FolderSyncInvite invite);

        Task SetContinuousAsync(int linkId, bool enabled);
        Task SwitchRoleAsync(int linkId);
        Task UpdateIgnorePathsAsync(int linkId, IEnumerable<string> ignorePaths);

        IReadOnlyList<FolderSyncLogEntry> GetLogs(string syncId);
        IReadOnlyList<FolderSyncSummaryEntry> GetSummary(string syncId);
    }
}
