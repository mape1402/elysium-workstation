using Elysium.WorkStation.Models;

namespace Elysium.WorkStation.Services
{
    public interface IRemoteToolCatalog
    {
        IReadOnlyList<RemoteToolDescriptor> GetTools();
    }
}
