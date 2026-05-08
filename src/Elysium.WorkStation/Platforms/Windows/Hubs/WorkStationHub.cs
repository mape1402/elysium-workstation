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

        public async Task SendRemoteCommand(
            string requestId,
            string syncId,
            string senderClientId,
            string tool,
            string action,
            string argsJson)
            => await Clients.Others.SendAsync(
                "ReceiveRemoteCommand",
                requestId,
                syncId,
                senderClientId,
                tool,
                action,
                argsJson);

        public async Task SendRemoteCommandResult(
            string requestId,
            string syncId,
            string tool,
            string action,
            int exitCode,
            string stdOut,
            string stdErr,
            string executorClientId)
            => await Clients.Others.SendAsync(
                "ReceiveRemoteCommandResult",
                requestId,
                syncId,
                tool,
                action,
                exitCode,
                stdOut,
                stdErr,
                executorClientId);

        public async Task SendRemoteTerminalInput(
            string sessionId,
            string syncId,
            string senderClientId,
            string commandText)
            => await Clients.Others.SendAsync(
                "ReceiveRemoteTerminalInput",
                sessionId,
                syncId,
                senderClientId,
                commandText);

        public async Task SendRemoteTerminalInterrupt(
            string sessionId,
            string syncId,
            string senderClientId)
            => await Clients.Others.SendAsync(
                "ReceiveRemoteTerminalInterrupt",
                sessionId,
                syncId,
                senderClientId);

        public async Task SendRemoteTerminalOutput(
            string sessionId,
            string syncId,
            string chunk,
            bool isError,
            bool isCompleted,
            int exitCode,
            string executorClientId)
            => await Clients.Others.SendAsync(
                "ReceiveRemoteTerminalOutput",
                sessionId,
                syncId,
                chunk,
                isError,
                isCompleted,
                exitCode,
                executorClientId);
    }
}
