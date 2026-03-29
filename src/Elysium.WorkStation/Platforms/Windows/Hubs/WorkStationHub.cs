using Microsoft.AspNetCore.SignalR;

namespace Elysium.WorkStation.Hubs
{
    public class WorkStationHub : Hub
    {
        public async Task SendMessage(string user, string message)
            => await Clients.All.SendAsync("ReceiveMessage", user, message);

        public async Task Broadcast(string eventName, object payload)
            => await Clients.All.SendAsync(eventName, payload);

        public async Task ClipboardSync(string text, string senderName)
            => await Clients.Others.SendAsync("ReceiveClipboard", text, senderName);

        public async Task AnnounceFile(string fileId, string fileName, long fileSize, string senderName)
            => await Clients.Others.SendAsync("ReceiveFileAnnouncement", fileId, fileName, fileSize, senderName);

        public async Task SendFolderSyncInvite(
            string inviteId,
            string syncId,
            string requesterClientId,
            string requesterName,
            string name,
            string description,
            string ignorePathsJson,
            string requesterFolderPath)
            => await Clients.Others.SendAsync(
                "ReceiveFolderSyncInvite",
                inviteId,
                syncId,
                requesterClientId,
                requesterName,
                name,
                description,
                ignorePathsJson,
                requesterFolderPath);

        public async Task RespondFolderSyncInvite(
            string inviteId,
            string syncId,
            bool accepted,
            string responderClientId,
            string responderName)
            => await Clients.Others.SendAsync(
                "ReceiveFolderSyncInviteResponse",
                inviteId,
                syncId,
                accepted,
                responderClientId,
                responderName);

        public async Task AnnounceFolderSyncChange(
            string syncId,
            string senderClientId,
            string action,
            string relativePath,
            string uploadId,
            long fileSize,
            string fileHash)
            => await Clients.Others.SendAsync(
                "ReceiveFolderSyncChange",
                syncId,
                senderClientId,
                action,
                relativePath,
                uploadId,
                fileSize,
                fileHash);

        public async Task AnnounceFolderSyncState(
            string syncId,
            bool enabled,
            string emitterClientId,
            string changedByClientId)
            => await Clients.Others.SendAsync(
                "ReceiveFolderSyncState",
                syncId,
                enabled,
                emitterClientId,
                changedByClientId);

        public async Task AnnounceFolderSyncUnlinked(
            string syncId,
            string changedByClientId)
            => await Clients.Others.SendAsync(
                "ReceiveFolderSyncUnlinked",
                syncId,
                changedByClientId);
    }
}
