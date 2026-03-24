namespace Elysium.WorkStation.Services
{
    public interface IKanbanCleanupService
    {
        Task StartAsync();
        Task StopAsync();
    }
}
