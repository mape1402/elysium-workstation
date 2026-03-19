namespace Elysium.WorkStation.Services
{
    public class SettingsService : ISettingsService
    {
        private const string ServerUrlKey = "server_url";
        private const string FileRetentionHoursKey = "file_retention_hours";
        private const string ClipboardRetentionHoursKey = "clipboard_retention_hours";
        private const string NotificationRetentionHoursKey = "notification_retention_hours";
        private const int DefaultRetentionHours = 72;

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
    }
}
