namespace Elysium.WorkStation.Services
{
    public class NotificationService : INotificationService
    {
        private readonly ITrayService _trayService;

        public NotificationService(ITrayService trayService)
        {
            _trayService = trayService;
        }

        public void Notify(string title, string message) => _trayService.ShowBalloon(title, message);
    }
}
