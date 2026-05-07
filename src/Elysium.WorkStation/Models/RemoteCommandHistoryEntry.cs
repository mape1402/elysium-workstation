namespace Elysium.WorkStation.Models
{
    public sealed class RemoteCommandHistoryEntry
    {
        public string RequestId { get; set; } = string.Empty;
        public string SyncId { get; set; } = string.Empty;
        public DateTime RequestedAt { get; set; } = DateTime.Now;
        public DateTime? CompletedAt { get; set; }
        public string Tool { get; set; } = string.Empty;
        public string Action { get; set; } = string.Empty;
        public string ArgsSummary { get; set; } = string.Empty;
        public string SenderClientId { get; set; } = string.Empty;
        public string ExecutorClientId { get; set; } = string.Empty;
        public string Status { get; set; } = "pending";
        public int? ExitCode { get; set; }
        public string StdOut { get; set; } = string.Empty;
        public string StdErr { get; set; } = string.Empty;
        public string StatusLabel =>
            string.Equals(Status, "ok", StringComparison.OrdinalIgnoreCase) ? "OK"
            : string.Equals(Status, "error", StringComparison.OrdinalIgnoreCase) ? "ERROR"
            : string.Equals(Status, "timeout", StringComparison.OrdinalIgnoreCase) ? "TIMEOUT"
            : "PENDING";
    }
}
