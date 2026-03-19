namespace Elysium.WorkStation.Models
{
    public class FileEntry
    {
        public string FileId { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public long FileSize { get; set; }
        public string SenderName { get; set; } = string.Empty;
        public bool IsFromSelf { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.Now;

        public string SizeDisplay => FileSize < 1_048_576
            ? $"{FileSize / 1024.0:F1} KB"
            : $"{FileSize / 1_048_576.0:F1} MB";

        public string SenderDisplay => IsFromSelf
            ? $"Tú · {Timestamp:HH:mm}"
            : $"{SenderName} · {Timestamp:HH:mm}";
    }
}
