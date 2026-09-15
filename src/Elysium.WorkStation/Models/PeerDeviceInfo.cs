namespace Elysium.WorkStation.Models
{
    public sealed class PeerDeviceInfo
    {
        public string ClientId { get; init; } = string.Empty;
        public string ClientName { get; init; } = string.Empty;
        public string MachineName { get; init; } = string.Empty;
        public string Role { get; init; } = string.Empty;
        public DateTime LastSeenUtc { get; init; } = DateTime.UtcNow;
    }
}
