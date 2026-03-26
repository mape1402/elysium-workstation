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
        private readonly ISecretVaultService _secretVaultService;
        private readonly IVariableRepository _variableRepository;
        private string _serverUrl;
        private int _fileRetentionHours;
        private int _clipboardRetentionHours;
        private int _notificationRetentionHours;
        private int _kanbanCleanupRetentionDays;
        private int _kanbanCleanupIntervalHours;
        private int _signalRReconnectMinutes;

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

        public int SignalRReconnectMinutes
        {
            get => _signalRReconnectMinutes;
            set { _signalRReconnectMinutes = value; OnPropertyChanged(); }
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
        public Command ResetPinCommand { get; }

        public SettingsPage(
            ISettingsService settingsService,
            IStartupService startupService,
            ISecretVaultService secretVaultService,
            IVariableRepository variableRepository)
        {
            _settingsService = settingsService;
            _startupService = startupService;
            _secretVaultService = secretVaultService;
            _variableRepository = variableRepository;
            _serverUrl = settingsService.ServerUrl;
            _fileRetentionHours = settingsService.FileRetentionHours;
            _clipboardRetentionHours = settingsService.ClipboardRetentionHours;
            _notificationRetentionHours = settingsService.NotificationRetentionHours;
            _kanbanCleanupRetentionDays = settingsService.KanbanCleanupRetentionDays;
            _kanbanCleanupIntervalHours = settingsService.KanbanCleanupIntervalHours;
            _signalRReconnectMinutes = settingsService.SignalRReconnectMinutes;
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
                _settingsService.SignalRReconnectMinutes = SignalRReconnectMinutes;

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

            ResetPinCommand = new Command(async () =>
            {
                var mode = await DisplayActionSheet(
                    "Resetear PIN",
                    "Cancelar",
                    null,
                    "Usar PIN anterior y conservar secretos",
                    "No tengo PIN anterior");

                if (string.IsNullOrWhiteSpace(mode) || mode == "Cancelar")
                    return;

                var resetWithoutOldPin = mode == "No tengo PIN anterior";
                string oldPin = string.Empty;
                if (!resetWithoutOldPin)
                {
                    if (!_secretVaultService.IsPinConfigured)
                    {
                        await DisplayAlert(
                            "Reset PIN",
                            "No hay PIN anterior configurado. Usa la opcion \"No tengo PIN anterior\".",
                            "OK");
                        return;
                    }

                    oldPin = await PromptPinAsync(
                        "PIN anterior",
                        "Ingresa el PIN actual para continuar.",
                        "PIN actual");
                    if (oldPin is null) return;
                    oldPin = oldPin.Trim();

                    if (!_secretVaultService.TryUnlockWithPin(oldPin))
                    {
                        await DisplayAlert("PIN", "El PIN anterior es incorrecto.", "OK");
                        return;
                    }
                }
                else
                {
                    var acceptLoss = await DisplayAlert(
                        "Confirmar limpieza",
                        "Si continuas sin PIN anterior, los secretos se convertiran en variables normales sin valor. Deseas continuar?",
                        "Aceptar",
                        "Cancelar");
                    if (!acceptLoss) return;
                }

                var newPin = await PromptPinAsync(
                    "Nuevo PIN",
                    "Ingresa el nuevo PIN (minimo 4 digitos numericos).",
                    "Nuevo PIN");
                if (newPin is null) return;
                newPin = newPin.Trim();

                if (!_secretVaultService.IsValidPin(newPin))
                {
                    await DisplayAlert("PIN", "El nuevo PIN debe tener al menos 4 digitos numericos.", "OK");
                    return;
                }

                var confirmPin = await PromptPinAsync(
                    "Confirmar PIN",
                    "Vuelve a ingresar el nuevo PIN.",
                    "Nuevo PIN");
                if (confirmPin is null) return;
                confirmPin = confirmPin.Trim();

                if (!string.Equals(newPin, confirmPin, StringComparison.Ordinal))
                {
                    await DisplayAlert("PIN", "Los PIN no coinciden.", "OK");
                    return;
                }

                if (!resetWithoutOldPin)
                {
                    try
                    {
                        var secrets = await _variableRepository.GetSecretVariablesAsync();
                        var plainById = new Dictionary<int, string>();

                        foreach (var secret in secrets)
                        {
                            plainById[secret.Id] = string.IsNullOrWhiteSpace(secret.EncryptedValue)
                                ? string.Empty
                                : _secretVaultService.Decrypt(secret.EncryptedValue);
                        }

                        if (!_secretVaultService.SetPin(newPin))
                        {
                            await DisplayAlert("PIN", "No fue posible configurar el nuevo PIN.", "OK");
                            return;
                        }

                        foreach (var secret in secrets)
                        {
                            secret.IsSecret = true;
                            secret.Value = string.Empty;
                            secret.EncryptedValue = _secretVaultService.Encrypt(plainById[secret.Id]);
                            await _variableRepository.SaveVariableAsync(secret);
                        }

                        _secretVaultService.Lock();
                        ShowFeedback("PIN actualizado y secretos recifrados correctamente.", Color.FromArgb("#1B5E20"));
                    }
                    catch (Exception ex)
                    {
                        _secretVaultService.Lock();
                        await DisplayAlert("Reset PIN", $"No fue posible recifrar secretos: {ex.Message}", "OK");
                    }

                    return;
                }

                try
                {
                    await _variableRepository.ResetSecretsAsync();
                    if (!_secretVaultService.SetPin(newPin))
                    {
                        await DisplayAlert("PIN", "No fue posible configurar el nuevo PIN.", "OK");
                        return;
                    }

                    _secretVaultService.Lock();
                    ShowFeedback("PIN actualizado. Los secretos quedaron vacios como variables normales.", Color.FromArgb("#E65100"));
                }
                catch (Exception ex)
                {
                    _secretVaultService.Lock();
                    await DisplayAlert("Reset PIN", $"No fue posible aplicar el cambio: {ex.Message}", "OK");
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

        private async Task<string?> PromptPinAsync(string title, string message, string placeholder)
        {
            var popup = new PinPromptPage(title, message, placeholder);
            await Navigation.PushModalAsync(popup);
            return await popup.ResultTask;
        }
    }
}

