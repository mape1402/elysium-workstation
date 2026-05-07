using Elysium.WorkStation.Models;

namespace Elysium.WorkStation.Services
{
    public interface IRemoteToolPlugin
    {
        RemoteToolDescriptor Describe();
    }
}
