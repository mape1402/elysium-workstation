namespace Elysium.WorkStation.Services
{
    public interface IEngineControlHostService
    {
        bool IsRunning { get; }
        string AppBridgePipeName { get; }
        Task StartAsync();
        Task StartPublicFallbackAsync();
        Task StopAsync();
    }
}
