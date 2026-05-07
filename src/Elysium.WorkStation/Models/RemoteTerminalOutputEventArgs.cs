namespace Elysium.WorkStation.Models
{
    public sealed class RemoteTerminalOutputEventArgs : EventArgs
    {
        public string SessionId { get; init; } = string.Empty;
        public string SyncId { get; init; } = string.Empty;
        public string Chunk { get; init; } = string.Empty;
        public bool IsError { get; init; }
        public bool IsCompleted { get; init; }
        public int ExitCode { get; init; }
        public string ExecutorClientId { get; init; } = string.Empty;
    }
}
