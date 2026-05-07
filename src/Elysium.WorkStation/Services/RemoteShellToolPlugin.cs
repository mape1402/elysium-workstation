using Elysium.WorkStation.Models;

namespace Elysium.WorkStation.Services
{
    public sealed class RemoteShellToolPlugin : IRemoteToolPlugin
    {
        public RemoteToolDescriptor Describe() =>
            new()
            {
                Id = "shell",
                Name = "Terminal remota",
                Description = "Consola remota en tiempo real (PowerShell receptor)",
                SupportsInteractiveTerminal = true
            };
    }
}
