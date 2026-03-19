namespace Elysium.WorkStation.Services
{
    public interface ITrayService
    {
        void Initialize(Action onShow, Action onExit);
        void Dispose();
    }
}
