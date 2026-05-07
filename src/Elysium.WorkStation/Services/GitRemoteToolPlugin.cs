using Elysium.WorkStation.Models;

namespace Elysium.WorkStation.Services
{
    public sealed class GitRemoteToolPlugin : IRemoteToolPlugin
    {
        public RemoteToolDescriptor Describe() =>
            new()
            {
                Id = "git",
                Name = "Git",
                Description = "Comandos rapidos git en el receptor",
                SupportsInteractiveTerminal = false,
                QuickActions =
                [
                    new RemoteToolQuickActionDescriptor { Id = "branch.create", Label = "Crear branch", Description = "git checkout -b <branch>" },
                    new RemoteToolQuickActionDescriptor { Id = "stage.add", Label = "Stage", Description = "git add <pathspec>" },
                    new RemoteToolQuickActionDescriptor { Id = "commit.create", Label = "Commit", Description = "git commit -m <msg>" },
                    new RemoteToolQuickActionDescriptor { Id = "push.current", Label = "Push", Description = "git push" }
                ]
            };
    }
}
