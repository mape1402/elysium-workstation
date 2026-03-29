namespace Elysium.WorkStation.Models
{
    public class FolderSyncInviteNotificationPayload
    {
        public string InviteId { get; set; } = string.Empty;
        public string SyncId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string IgnorePathsJson { get; set; } = "[]";
        public string RequesterClientId { get; set; } = string.Empty;
        public string RequesterName { get; set; } = string.Empty;
        public string RequesterFolderPath { get; set; } = string.Empty;
    }
}
