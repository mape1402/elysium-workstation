namespace Elysium.WorkStation.Services
{
    public interface ISettingsService
    {
        string ServerUrl { get; set; }
        string HubUrl { get; }
        string StatusApiUrl { get; }
        bool IsConfigured { get; }
        int ServerPort { get; }
        int FileRetentionHours { get; set; }
    }
}
