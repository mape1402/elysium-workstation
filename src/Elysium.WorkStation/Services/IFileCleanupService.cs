namespace Elysium.WorkStation.Services
{
    public interface IFileCleanupService
    {
        Task StartAsync();
        Task StopAsync();
    }
}
