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
        int ClipboardRetentionHours { get; set; }
        int NotificationRetentionHours { get; set; }

        int KanbanCleanupRetentionDays { get; set; }
        int KanbanCleanupIntervalHours { get; set; }
        string ThemeMode { get; set; }

        bool MouseEnabled { get; set; }
        bool MouseUseGeneralSchedule { get; set; }
        TimeSpan MouseGeneralStartTime { get; set; }
        TimeSpan MouseGeneralEndTime { get; set; }
        List<Models.MouseScheduleEntry> MouseDaySchedules { get; set; }
        // SignalR reconnect settings
        int SignalRReconnectMinutes { get; set; }
        TimeSpan SignalRReconnectDelay { get; }
    }
}
