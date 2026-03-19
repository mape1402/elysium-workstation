namespace Elysium.WorkStation.Services
{
    public interface IWebHostService
    {
        string BaseUrl { get; }
        bool IsRunning { get; }
        Task StartAsync(int port = 5050);
        Task StopAsync();
    }
}
