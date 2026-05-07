namespace Elysium.WorkStation.Models
{
    public sealed class RemoteCommandResultEventArgs : EventArgs
    {
        public string RequestId { get; init; } = string.Empty;
        public string SyncId { get; init; } = string.Empty;
        public string Tool { get; init; } = string.Empty;
        public string Action { get; init; } = string.Empty;
        public int ExitCode { get; init; }
        public string StdOut { get; init; } = string.Empty;
        public string StdErr { get; init; } = string.Empty;
        public bool Success => ExitCode == 0;
        public string ExecutorClientId { get; init; } = string.Empty;
    }
}
