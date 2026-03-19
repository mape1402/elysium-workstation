namespace Elysium.WorkStation.Models
{
    public class ClipboardEntry
    {
        public string Text { get; init; }
        public DateTime Timestamp { get; init; }
        public string SenderName { get; init; }
        public bool IsFromSelf { get; init; }

        public string SenderDisplay =>
            $"{(IsFromSelf ? "📤 Tú" : $"📥 {SenderName}")} · {Timestamp:HH:mm:ss}";
    }
}
