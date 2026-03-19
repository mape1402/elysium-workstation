using System.ComponentModel.DataAnnotations.Schema;

namespace Elysium.WorkStation.Models
{
    public class ClipboardEntry
    {
        public int Id { get; set; }
        public string Text { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
        public string SenderName { get; set; } = string.Empty;
        public bool IsFromSelf { get; set; }

        [NotMapped]
        public string SenderDisplay =>
            $"{(IsFromSelf ? "📤 Tú" : $"📥 {SenderName}")} · {Timestamp:HH:mm:ss}";
    }
}
