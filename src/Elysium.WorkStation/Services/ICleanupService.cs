namespace Elysium.WorkStation.Services
{
    public interface ICleanupService
    {
        Task StartAsync();
        Task StopAsync();
    }
}
