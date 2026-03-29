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
        private const string ThemeModeKey = "theme_mode";
        private const int DefaultRetentionHours = 72;
        private const int DefaultKanbanCleanupRetentionDays = 7;
        private const int DefaultKanbanCleanupIntervalHours = 1;
        private const string SignalRReconnectMinutesKey = "signalr_reconnect_minutes";
        private const int DefaultSignalRReconnectMinutes = 1;
        private const string ProfileFirstNameKey = "profile_first_name";
        private const string ProfileLastNameKey = "profile_last_name";
        private const string ProfilePhotoPathKey = "profile_photo_path";
        private const string ProfileIsRegisteredKey = "profile_is_registered";

        public string ServerUrl
        {
            get
            {
                var scoped = (ScopedPreferences.Get(ServerUrlKey, string.Empty) ?? string.Empty).Trim();
                if (!string.IsNullOrWhiteSpace(scoped))
                {
                    return scoped.TrimEnd('/');
                }

                return string.Empty;
            }
            set => ScopedPreferences.Set(ServerUrlKey, (value ?? string.Empty).Trim().TrimEnd('/'));
        }

        public bool IsConfigured => !string.IsNullOrWhiteSpace(ServerUrl);

        public string HubUrl => $"{ServerUrl}/hubs/workstation";
        public string StatusApiUrl => $"{ServerUrl}/api/status";

        public int ServerPort =>
            Uri.TryCreate(ServerUrl, UriKind.Absolute, out var uri) ? uri.Port : -1;

        public int FileRetentionHours
        {
            get => ScopedPreferences.Get(FileRetentionHoursKey, DefaultRetentionHours);
            set => ScopedPreferences.Set(FileRetentionHoursKey, Math.Max(1, value));
        }

        public int ClipboardRetentionHours
        {
            get => ScopedPreferences.Get(ClipboardRetentionHoursKey, DefaultRetentionHours);
            set => ScopedPreferences.Set(ClipboardRetentionHoursKey, Math.Max(1, value));
        }

        public int NotificationRetentionHours
        {
            get => ScopedPreferences.Get(NotificationRetentionHoursKey, DefaultRetentionHours);
            set => ScopedPreferences.Set(NotificationRetentionHoursKey, Math.Max(1, value));
        }

        public int KanbanCleanupRetentionDays
        {
            get => ScopedPreferences.Get(KanbanCleanupRetentionDaysKey, DefaultKanbanCleanupRetentionDays);
            set => ScopedPreferences.Set(KanbanCleanupRetentionDaysKey, Math.Max(1, value));
        }

        public int KanbanCleanupIntervalHours
        {
            get => ScopedPreferences.Get(KanbanCleanupIntervalHoursKey, DefaultKanbanCleanupIntervalHours);
            set => ScopedPreferences.Set(KanbanCleanupIntervalHoursKey, Math.Max(1, value));
        }

        public bool MouseEnabled
        {
            get => ScopedPreferences.Get(MouseEnabledKey, true);
            set => ScopedPreferences.Set(MouseEnabledKey, value);
        }

        public bool MouseUseGeneralSchedule
        {
            get => ScopedPreferences.Get(MouseUseGeneralScheduleKey, true);
            set => ScopedPreferences.Set(MouseUseGeneralScheduleKey, value);
        }

        public TimeSpan MouseGeneralStartTime
        {
            get => TimeSpan.TryParse(ScopedPreferences.Get(MouseGeneralStartKey, "08:00"), out var t) ? t : new(8, 0, 0);
            set => ScopedPreferences.Set(MouseGeneralStartKey, value.ToString(@"hh\:mm"));
        }

        public TimeSpan MouseGeneralEndTime
        {
            get => TimeSpan.TryParse(ScopedPreferences.Get(MouseGeneralEndKey, "18:00"), out var t) ? t : new(18, 0, 0);
            set => ScopedPreferences.Set(MouseGeneralEndKey, value.ToString(@"hh\:mm"));
        }

        // SignalR reconnect delay in minutes (used by client retry policies)
        public int SignalRReconnectMinutes
        {
            get => ScopedPreferences.Get(SignalRReconnectMinutesKey, DefaultSignalRReconnectMinutes);
            set => ScopedPreferences.Set(SignalRReconnectMinutesKey, Math.Max(1, value));
        }

        public TimeSpan SignalRReconnectDelay => TimeSpan.FromMinutes(SignalRReconnectMinutes);

        public string ProfileFirstName
        {
            get => ScopedPreferences.Get(ProfileFirstNameKey, string.Empty);
            set => ScopedPreferences.Set(ProfileFirstNameKey, (value ?? string.Empty).Trim());
        }

        public string ProfileLastName
        {
            get => ScopedPreferences.Get(ProfileLastNameKey, string.Empty);
            set => ScopedPreferences.Set(ProfileLastNameKey, (value ?? string.Empty).Trim());
        }

        public string ProfilePhotoPath
        {
            get => ScopedPreferences.Get(ProfilePhotoPathKey, string.Empty);
            set => ScopedPreferences.Set(ProfilePhotoPathKey, value ?? string.Empty);
        }

        public bool ProfileIsRegistered
        {
            get => ScopedPreferences.Get(ProfileIsRegisteredKey, false);
            set => ScopedPreferences.Set(ProfileIsRegisteredKey, value);
        }

        public string SqliteDbPath
        {
            get => DatabasePathProvider.GetPath();
            set => DatabasePathProvider.SetPath(value);
        }

        public string ThemeMode
        {
            get
            {
                var mode = ScopedPreferences.Get(ThemeModeKey, "Light");
                return string.Equals(mode, "Dark", StringComparison.OrdinalIgnoreCase) ? "Dark" : "Light";
            }
            set
            {
                var normalized = string.Equals(value, "Dark", StringComparison.OrdinalIgnoreCase) ? "Dark" : "Light";
                ScopedPreferences.Set(ThemeModeKey, normalized);
            }
        }

        public List<MouseScheduleEntry> MouseDaySchedules
        {
            get
            {
                var json = ScopedPreferences.Get(MouseDaySchedulesKey, string.Empty);
                if (!string.IsNullOrEmpty(json))
                {
                    try { return JsonSerializer.Deserialize<List<MouseScheduleEntry>>(json); }
                    catch { /* corrupted, return defaults */ }
                }
                return DefaultDaySchedules();
            }
            set => ScopedPreferences.Set(MouseDaySchedulesKey, JsonSerializer.Serialize(value));
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
