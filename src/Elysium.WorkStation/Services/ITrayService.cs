namespace Elysium.WorkStation.Services
{
    public interface ITrayService
    {
        void Initialize(Action onShow, Action onExit, Action onQuickNote);
        void ShowBalloon(string title, string message);
        void Dispose();
    }
}
