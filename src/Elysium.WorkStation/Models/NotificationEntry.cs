using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;

namespace Elysium.WorkStation.Models
{
    public class NotificationEntry
    {
        private const string FolderSyncInvitePrefix = "__FOLDER_SYNC_INVITE__:";

        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public bool IsRead { get; set; }

        [NotMapped]
        public string TimeDisplay => Timestamp.ToString("dd/MM · HH:mm");

        [NotMapped]
        public bool IsFolderSyncInvite => TryGetFolderSyncInvitePayload(out _);

        [NotMapped]
        public bool IsGenericNotification => !IsFolderSyncInvite;

        [NotMapped]
        public string DisplayMessage =>
            TryGetFolderSyncInvitePayload(out var payload)
                ? $"{payload.RequesterName} solicita vincular la carpeta '{payload.Name}'."
                : Message;

        [NotMapped]
        public string FolderSyncDescription =>
            TryGetFolderSyncInvitePayload(out var payload) ? payload.Description : string.Empty;

        [NotMapped]
        public bool HasFolderSyncDescription => !string.IsNullOrWhiteSpace(FolderSyncDescription);

        [NotMapped]
        public string FolderSyncRequester =>
            TryGetFolderSyncInvitePayload(out var payload) ? payload.RequesterName : string.Empty;

        [NotMapped]
        public string FolderSyncOriginPath =>
            TryGetFolderSyncInvitePayload(out var payload) ? payload.RequesterFolderPath : string.Empty;

        [NotMapped]
        public bool HasFolderSyncOriginPath => !string.IsNullOrWhiteSpace(FolderSyncOriginPath);

        public static string BuildFolderSyncInviteMessage(FolderSyncInviteNotificationPayload payload)
        {
            if (payload is null)
            {
                return string.Empty;
            }

            return FolderSyncInvitePrefix + JsonSerializer.Serialize(payload);
        }

        public bool TryGetFolderSyncInvitePayload(out FolderSyncInviteNotificationPayload payload)
        {
            payload = null;
            if (string.IsNullOrWhiteSpace(Message) || !Message.StartsWith(FolderSyncInvitePrefix, StringComparison.Ordinal))
            {
                return false;
            }

            var json = Message[FolderSyncInvitePrefix.Length..];
            if (string.IsNullOrWhiteSpace(json))
            {
                return false;
            }

            try
            {
                payload = JsonSerializer.Deserialize<FolderSyncInviteNotificationPayload>(json);
            }
            catch
            {
                payload = null;
            }

            return payload is not null;
        }
    }
}
