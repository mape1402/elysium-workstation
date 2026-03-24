namespace Elysium.WorkStation.Services
{
    public interface IToastService
    {
        Task ShowAsync(string message, int durationMs = 2000);
    }
}
