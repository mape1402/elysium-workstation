using System.ComponentModel.DataAnnotations.Schema;

namespace Elysium.WorkStation.Models
{
    public class FileEntry
    {
        public int Id { get; set; }
        public string FileId { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public long FileSize { get; set; }
        public string SenderName { get; set; } = string.Empty;
        public bool IsFromSelf { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public string SourcePath { get; set; }

        [NotMapped]
        public string SizeDisplay => FileSize < 1_048_576
            ? $"{FileSize / 1024.0:F1} KB"
            : $"{FileSize / 1_048_576.0:F1} MB";

        [NotMapped]
        public string SenderDisplay => IsFromSelf
            ? $"Tú · {Timestamp:HH:mm}"
            : $"{SenderName} · {Timestamp:HH:mm}";
    }
}
