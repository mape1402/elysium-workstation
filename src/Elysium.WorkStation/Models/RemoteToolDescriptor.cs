namespace Elysium.WorkStation.Models
{
    public sealed class RemoteToolDescriptor
    {
        public string Id { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public string Description { get; init; } = string.Empty;
        public bool SupportsInteractiveTerminal { get; init; }
        public IReadOnlyList<RemoteToolQuickActionDescriptor> QuickActions { get; init; } = [];
    }
}
