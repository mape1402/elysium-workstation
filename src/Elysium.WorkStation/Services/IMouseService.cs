namespace Elysium.WorkStation.Services
{
    public interface IMouseService
    {
        void Start(int intervalSeconds = 30);
     
        void Stop();
    }
}
