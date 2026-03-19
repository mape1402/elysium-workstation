namespace Elysium.WorkStation.Services
{
    public class SettingsService : ISettingsService
    {
        private const string ServerUrlKey = "server_url";

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
    }
}
