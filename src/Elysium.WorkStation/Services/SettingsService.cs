namespace Elysium.WorkStation.Services
{
    using System.Text.Json;
    using Elysium.WorkStation.Models;

    public class SettingsService : ISettingsService
    {
        private const string ServerUrlKey = "server_url";
        private const string FileRetentionHoursKey = "file_retention_hours";
        private const string ClipboardRetentionHoursKey = "clipboard_retention_hours";
        private const string NotificationRetentionHoursKey = "notification_retention_hours";
        private const string KanbanCleanupRetentionDaysKey = "kanban_cleanup_retention_days";
        private const string KanbanCleanupIntervalHoursKey = "kanban_cleanup_interval_hours";
        private const string MouseEnabledKey = "mouse_enabled";
        private const string MouseUseGeneralScheduleKey = "mouse_use_general_schedule";
        private const string MouseGeneralStartKey = "mouse_general_start";
        private const string MouseGeneralEndKey = "mouse_general_end";
        private const string MouseDaySchedulesKey = "mouse_day_schedules";
        private const int DefaultRetentionHours = 72;
        private const int DefaultKanbanCleanupRetentionDays = 7;
        private const int DefaultKanbanCleanupIntervalHours = 1;
        private const string SignalRReconnectMinutesKey = "signalr_reconnect_minutes";
        private const int DefaultSignalRReconnectMinutes = 1;

        public string ServerUrl
        {
            get => Preferences.Default.Get(ServerUrlKey, string.Empty);
            set => Preferences.Default.Set(ServerUrlKey, value.TrimEnd('/'));
        }

        public bool IsConfigured => !string.IsNullOrWhiteSpace(ServerUrl);

        public string HubUrl => $"{ServerUrl}/hubs/workstation";
        public string StatusApiUrl => $"{ServerUrl}/api/status";

        public int ServerPort =>
            Uri.TryCreate(ServerUrl, UriKind.Absolute, out var uri) ? uri.Port : -1;

        public int FileRetentionHours
        {
            get => Preferences.Default.Get(FileRetentionHoursKey, DefaultRetentionHours);
            set => Preferences.Default.Set(FileRetentionHoursKey, Math.Max(1, value));
        }

        public int ClipboardRetentionHours
        {
            get => Preferences.Default.Get(ClipboardRetentionHoursKey, DefaultRetentionHours);
            set => Preferences.Default.Set(ClipboardRetentionHoursKey, Math.Max(1, value));
        }

        public int NotificationRetentionHours
        {
            get => Preferences.Default.Get(NotificationRetentionHoursKey, DefaultRetentionHours);
            set => Preferences.Default.Set(NotificationRetentionHoursKey, Math.Max(1, value));
        }

        public int KanbanCleanupRetentionDays
        {
            get => Preferences.Default.Get(KanbanCleanupRetentionDaysKey, DefaultKanbanCleanupRetentionDays);
            set => Preferences.Default.Set(KanbanCleanupRetentionDaysKey, Math.Max(1, value));
        }

        public int KanbanCleanupIntervalHours
        {
            get => Preferences.Default.Get(KanbanCleanupIntervalHoursKey, DefaultKanbanCleanupIntervalHours);
            set => Preferences.Default.Set(KanbanCleanupIntervalHoursKey, Math.Max(1, value));
        }

        public bool MouseEnabled
        {
            get => Preferences.Default.Get(MouseEnabledKey, true);
            set => Preferences.Default.Set(MouseEnabledKey, value);
        }

        public bool MouseUseGeneralSchedule
        {
            get => Preferences.Default.Get(MouseUseGeneralScheduleKey, true);
            set => Preferences.Default.Set(MouseUseGeneralScheduleKey, value);
        }

        public TimeSpan MouseGeneralStartTime
        {
            get => TimeSpan.TryParse(Preferences.Default.Get(MouseGeneralStartKey, "08:00"), out var t) ? t : new(8, 0, 0);
            set => Preferences.Default.Set(MouseGeneralStartKey, value.ToString(@"hh\:mm"));
        }

        public TimeSpan MouseGeneralEndTime
        {
            get => TimeSpan.TryParse(Preferences.Default.Get(MouseGeneralEndKey, "18:00"), out var t) ? t : new(18, 0, 0);
            set => Preferences.Default.Set(MouseGeneralEndKey, value.ToString(@"hh\:mm"));
        }

        // SignalR reconnect delay in minutes (used by client retry policies)
        public int SignalRReconnectMinutes
        {
            get => Preferences.Default.Get(SignalRReconnectMinutesKey, DefaultSignalRReconnectMinutes);
            set => Preferences.Default.Set(SignalRReconnectMinutesKey, Math.Max(1, value));
        }

        public TimeSpan SignalRReconnectDelay => TimeSpan.FromMinutes(SignalRReconnectMinutes);

        public List<MouseScheduleEntry> MouseDaySchedules
        {
            get
            {
                var json = Preferences.Default.Get(MouseDaySchedulesKey, string.Empty);
                if (!string.IsNullOrEmpty(json))
                {
                    try { return JsonSerializer.Deserialize<List<MouseScheduleEntry>>(json); }
                    catch { /* corrupted, return defaults */ }
                }
                return DefaultDaySchedules();
            }
            set => Preferences.Default.Set(MouseDaySchedulesKey, JsonSerializer.Serialize(value));
        }

        private static List<MouseScheduleEntry> DefaultDaySchedules() =>
        [
            new() { Day = DayOfWeek.Monday,    IsEnabled = true,  StartTime = new(8,0,0), EndTime = new(18,0,0) },
            new() { Day = DayOfWeek.Tuesday,   IsEnabled = true,  StartTime = new(8,0,0), EndTime = new(18,0,0) },
            new() { Day = DayOfWeek.Wednesday, IsEnabled = true,  StartTime = new(8,0,0), EndTime = new(18,0,0) },
            new() { Day = DayOfWeek.Thursday,  IsEnabled = true,  StartTime = new(8,0,0), EndTime = new(18,0,0) },
            new() { Day = DayOfWeek.Friday,    IsEnabled = true,  StartTime = new(8,0,0), EndTime = new(18,0,0) },
            new() { Day = DayOfWeek.Saturday,  IsEnabled = false, StartTime = new(8,0,0), EndTime = new(18,0,0) },
            new() { Day = DayOfWeek.Sunday,    IsEnabled = false, StartTime = new(8,0,0), EndTime = new(18,0,0) },
        ];
    }
}
