using Elysium.WorkStation.Models;
using Elysium.WorkStation.Services;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace Elysium.WorkStation.Views
{
    public class DayScheduleViewModel : INotifyPropertyChanged
    {
        private static readonly Dictionary<DayOfWeek, string> DayNames = new()
        {
            [DayOfWeek.Monday]    = "Lunes",
            [DayOfWeek.Tuesday]   = "Martes",
            [DayOfWeek.Wednesday] = "Miércoles",
            [DayOfWeek.Thursday]  = "Jueves",
            [DayOfWeek.Friday]    = "Viernes",
            [DayOfWeek.Saturday]  = "Sábado",
            [DayOfWeek.Sunday]    = "Domingo",
        };

        private bool _isEnabled;
        private TimeSpan _startTime;
        private TimeSpan _endTime;

        public DayOfWeek Day { get; init; }
        public string DayName => DayNames.TryGetValue(Day, out var n) ? n : Day.ToString();

        public bool IsEnabled
        {
            get => _isEnabled;
            set { _isEnabled = value; PropertyChanged?.Invoke(this, new(nameof(IsEnabled))); }
        }

        public TimeSpan StartTime
        {
            get => _startTime;
            set { _startTime = value; PropertyChanged?.Invoke(this, new(nameof(StartTime))); }
        }

        public TimeSpan EndTime
        {
            get => _endTime;
            set { _endTime = value; PropertyChanged?.Invoke(this, new(nameof(EndTime))); }
        }

        public event PropertyChangedEventHandler PropertyChanged;
    }

    public partial class SettingsPage : ContentPage
    {
        private readonly ISettingsService _settingsService;
        private readonly IStartupService _startupService;
        private string _serverUrl;
        private int _fileRetentionHours;
        private int _clipboardRetentionHours;
        private int _notificationRetentionHours;
        private int _kanbanCleanupRetentionDays;
        private int _kanbanCleanupIntervalHours;

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

        public int ClipboardRetentionHours
        {
            get => _clipboardRetentionHours;
            set
            {
                _clipboardRetentionHours = value;
                OnPropertyChanged();
            }
        }

        public int NotificationRetentionHours
        {
            get => _notificationRetentionHours;
            set
            {
                _notificationRetentionHours = value;
                OnPropertyChanged();
            }
        }

        public int KanbanCleanupRetentionDays
        {
            get => _kanbanCleanupRetentionDays;
            set
            {
                _kanbanCleanupRetentionDays = value;
                OnPropertyChanged();
            }
        }

        public int KanbanCleanupIntervalHours
        {
            get => _kanbanCleanupIntervalHours;
            set
            {
                _kanbanCleanupIntervalHours = value;
                OnPropertyChanged();
            }
        }

        public string FeedbackText { get; private set; } = string.Empty;
        public Color FeedbackColor { get; private set; } = Colors.Transparent;
        public bool HasFeedback { get; private set; }

        private bool _mouseEnabled;
        public bool MouseEnabled
        {
            get => _mouseEnabled;
            set
            {
                _mouseEnabled = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ShowGeneralSchedule));
                OnPropertyChanged(nameof(ShowDaySchedule));
            }
        }

        private bool _mouseUseGeneralSchedule;
        public bool MouseUseGeneralSchedule
        {
            get => _mouseUseGeneralSchedule;
            set
            {
                _mouseUseGeneralSchedule = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ShowGeneralSchedule));
                OnPropertyChanged(nameof(ShowDaySchedule));
            }
        }

        private TimeSpan _mouseGeneralStartTime;
        public TimeSpan MouseGeneralStartTime
        {
            get => _mouseGeneralStartTime;
            set { _mouseGeneralStartTime = value; OnPropertyChanged(); }
        }

        private TimeSpan _mouseGeneralEndTime;
        public TimeSpan MouseGeneralEndTime
        {
            get => _mouseGeneralEndTime;
            set { _mouseGeneralEndTime = value; OnPropertyChanged(); }
        }

        public bool ShowGeneralSchedule => MouseEnabled && MouseUseGeneralSchedule;
        public bool ShowDaySchedule => MouseEnabled && !MouseUseGeneralSchedule;

        public ObservableCollection<DayScheduleViewModel> MouseDaySchedules { get; } = [];

        private bool _startWithWindows;
        public bool StartWithWindows
        {
            get => _startWithWindows;
            set { _startWithWindows = value; OnPropertyChanged(); }
        }

        public Command SaveCommand { get; }
        public Command TestCommand { get; }

        public SettingsPage(ISettingsService settingsService, IStartupService startupService)
        {
            _settingsService = settingsService;
            _startupService = startupService;
            _serverUrl = settingsService.ServerUrl;
            _fileRetentionHours = settingsService.FileRetentionHours;
            _clipboardRetentionHours = settingsService.ClipboardRetentionHours;
            _notificationRetentionHours = settingsService.NotificationRetentionHours;
            _kanbanCleanupRetentionDays = settingsService.KanbanCleanupRetentionDays;
            _kanbanCleanupIntervalHours = settingsService.KanbanCleanupIntervalHours;
            _startWithWindows = startupService.IsEnabled;

            _mouseEnabled = settingsService.MouseEnabled;
            _mouseUseGeneralSchedule = settingsService.MouseUseGeneralSchedule;
            _mouseGeneralStartTime = settingsService.MouseGeneralStartTime;
            _mouseGeneralEndTime = settingsService.MouseGeneralEndTime;

            foreach (var entry in settingsService.MouseDaySchedules)
                MouseDaySchedules.Add(new DayScheduleViewModel
                {
                    Day = entry.Day,
                    IsEnabled = entry.IsEnabled,
                    StartTime = entry.StartTime,
                    EndTime = entry.EndTime
                });

            SaveCommand = new Command(async () =>
            {
                if (!IsValidUrl(ServerUrl))
                {
                    ShowFeedback("⚠️  URL inválida. Usa el formato http://host:puerto", Color.FromArgb("#E65100"));
                    return;
                }
                _settingsService.ServerUrl = ServerUrl;
                _settingsService.FileRetentionHours = FileRetentionHours;
                _settingsService.ClipboardRetentionHours = ClipboardRetentionHours;
                _settingsService.NotificationRetentionHours = NotificationRetentionHours;
                _settingsService.KanbanCleanupRetentionDays = KanbanCleanupRetentionDays;
                _settingsService.KanbanCleanupIntervalHours = KanbanCleanupIntervalHours;

                _settingsService.MouseEnabled = MouseEnabled;
                _settingsService.MouseUseGeneralSchedule = MouseUseGeneralSchedule;
                _settingsService.MouseGeneralStartTime = MouseGeneralStartTime;
                _settingsService.MouseGeneralEndTime = MouseGeneralEndTime;
                _settingsService.MouseDaySchedules = MouseDaySchedules
                    .Select(d => new MouseScheduleEntry
                    {
                        Day = d.Day,
                        IsEnabled = d.IsEnabled,
                        StartTime = d.StartTime,
                        EndTime = d.EndTime
                    }).ToList();

                if (StartWithWindows)
                    _startupService.Enable();
                else
                    _startupService.Disable();

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

