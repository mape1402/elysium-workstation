namespace Elysium.WorkStation.Models
{
    public class FolderSyncSummaryEntry
    {
        public string RelativePath { get; set; } = string.Empty;
        public int SentCount { get; set; }
        public int ReceivedCount { get; set; }
        public int DeletedCount { get; set; }
        public DateTime LastUpdatedAt { get; set; } = DateTime.Now;
        public string LastAction { get; set; } = string.Empty;
    }
}
