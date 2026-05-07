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
        event EventHandler<RemoteCommandResultEventArgs> RemoteCommandResultReceived;
        event EventHandler<RemoteTerminalOutputEventArgs> RemoteTerminalOutputReceived;

        Task StartAsync(string hubUrl);
        Task StopAsync();
        Task ReloadAsync();

        Task<FolderSyncLink> CreateSyncRequestAsync(
            string name,
            string description,
            string localFolderPath,
            IEnumerable<string> ignorePaths);

        Task SendPairRequestAsync(int linkId);
        Task<FolderSyncLink> AcceptInviteAsync(FolderSyncInvite invite, string localFolderPath);
        Task RejectInviteAsync(FolderSyncInvite invite);

        Task SetContinuousAsync(int linkId, bool enabled);
        Task SwitchRoleAsync(int linkId);
        Task UpdateIgnorePathsAsync(int linkId, IEnumerable<string> ignorePaths);
        Task DeleteSyncAsync(int linkId);
        Task RequestRemoteGitCreateBranchAsync(int linkId, string branchName);
        Task RequestRemoteGitAddAsync(int linkId, string pathspec);
        Task RequestRemoteGitCommitAsync(int linkId, string message);
        Task RequestRemoteGitPushAsync(int linkId);
        Task SendRemoteTerminalCommandAsync(int linkId, string sessionId, string commandText);

        IReadOnlyList<FolderSyncLogEntry> GetLogs(string syncId);
        IReadOnlyList<FolderSyncSummaryEntry> GetSummary(string syncId);
        IReadOnlyList<RemoteCommandHistoryEntry> GetRemoteCommandHistory(string syncId);
    }
}
