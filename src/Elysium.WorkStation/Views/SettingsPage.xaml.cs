using Elysium.WorkStation.Services;

namespace Elysium.WorkStation.Views
{
    public partial class SettingsPage : ContentPage
    {
        private readonly ISettingsService _settingsService;
        private string _serverUrl;
        private int _fileRetentionHours;

        public string ServerUrl
        {
            get => _serverUrl;
            set
            {
                _serverUrl = value;
                OnPropertyChanged();
            }
        }

        public int FileRetentionHours
        {
            get => _fileRetentionHours;
            set
            {
                _fileRetentionHours = value;
                OnPropertyChanged();
            }
        }

        public string FeedbackText { get; private set; } = string.Empty;
        public Color FeedbackColor { get; private set; } = Colors.Transparent;
        public bool HasFeedback { get; private set; }

        public Command SaveCommand { get; }
        public Command TestCommand { get; }

        public SettingsPage(ISettingsService settingsService)
        {
            _settingsService = settingsService;
            _serverUrl = settingsService.ServerUrl;
            _fileRetentionHours = settingsService.FileRetentionHours;

            SaveCommand = new Command(async () =>
            {
                if (!IsValidUrl(ServerUrl))
                {
                    ShowFeedback("⚠️  URL inválida. Usa el formato http://host:puerto", Color.FromArgb("#E65100"));
                    return;
                }
                _settingsService.ServerUrl = ServerUrl;
                _settingsService.FileRetentionHours = FileRetentionHours;
                ShowFeedback("✅  Configuración guardada correctamente.", Color.FromArgb("#1B5E20"));
                await Task.Delay(600);
                await Shell.Current.GoToAsync("..");
            });

            TestCommand = new Command(async () =>
            {
                if (!IsValidUrl(ServerUrl))
                {
                    ShowFeedback("⚠️  URL inválida. Usa el formato http://host:puerto", Color.FromArgb("#E65100"));
                    return;
                }
                ShowFeedback("⏳  Probando conexión...", Color.FromArgb("#1565C0"));
                try
                {
                    using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
                    var response = await client.GetAsync(ServerUrl.TrimEnd('/') + "/api/status");
                    if (response.IsSuccessStatusCode)
                        ShowFeedback("✅  Servidor alcanzable.", Color.FromArgb("#1B5E20"));
                    else
                        ShowFeedback($"⚠️  Respuesta inesperada: {(int)response.StatusCode}.", Color.FromArgb("#E65100"));
                }
                catch
                {
                    ShowFeedback("❌  No se pudo conectar al servidor.", Color.FromArgb("#B71C1C"));
                }
            });

            InitializeComponent();
            BindingContext = this;
        }

        private static bool IsValidUrl(string url) =>
            !string.IsNullOrWhiteSpace(url) &&
            Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
            (uri.Scheme == "http" || uri.Scheme == "https");

        private void ShowFeedback(string text, Color color)
        {
            FeedbackText = text;
            FeedbackColor = color;
            HasFeedback = true;
            OnPropertyChanged(nameof(FeedbackText));
            OnPropertyChanged(nameof(FeedbackColor));
            OnPropertyChanged(nameof(HasFeedback));
        }
    }
}

