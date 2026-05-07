using Elysium.WorkStation.Models;

namespace Elysium.WorkStation.Services
{
    public sealed class RemoteToolCatalog : IRemoteToolCatalog
    {
        private readonly IReadOnlyList<IRemoteToolPlugin> _plugins;

        public RemoteToolCatalog(IEnumerable<IRemoteToolPlugin> plugins)
        {
            _plugins = (plugins ?? []).ToList();
        }

        public IReadOnlyList<RemoteToolDescriptor> GetTools() =>
            _plugins
                .Select(plugin => plugin.Describe())
                .OrderBy(tool => tool.Name)
                .ToList();
    }
}
