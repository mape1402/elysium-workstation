namespace Elysium.WorkStation.Services
{
    public class NullNotificationService : INotificationService
    {
        public void Notify(string title, string message) { }
    }
}
