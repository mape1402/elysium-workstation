namespace Elysium.WorkStation.Models
{
    public class FolderSyncLogEntry
    {
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public string SyncId { get; set; } = string.Empty;
        public string RelativePath { get; set; } = string.Empty;
        public string Action { get; set; } = string.Empty;
        public bool IsOutgoing { get; set; }
        public string Message { get; set; } = string.Empty;
    }
}
