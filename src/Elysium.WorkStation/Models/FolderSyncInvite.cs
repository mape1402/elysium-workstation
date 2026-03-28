namespace Elysium.WorkStation.Models
{
    public class FolderSyncInvite
    {
        public string InviteId { get; set; } = string.Empty;
        public string SyncId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string IgnorePathsJson { get; set; } = "[]";
        public string RequesterClientId { get; set; } = string.Empty;
        public string RequesterName { get; set; } = string.Empty;
        public DateTime ReceivedAt { get; set; } = DateTime.Now;
    }
}
