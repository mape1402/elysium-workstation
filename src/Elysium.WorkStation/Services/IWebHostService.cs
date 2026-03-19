namespace Elysium.WorkStation.Services
{
    public interface IWebHostService
    {
        string BaseUrl { get; }
        bool IsRunning { get; }
        Task StartAsync();
        Task StopAsync();
    }
}
