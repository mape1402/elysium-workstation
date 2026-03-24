namespace Elysium.WorkStation.Services
{
    public interface IStartupService
    {
        bool IsEnabled { get; }
        void Enable();
        void Disable();
    }
}
