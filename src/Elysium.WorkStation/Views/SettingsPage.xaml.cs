using Elysium.WorkStation.Data;
using Elysium.WorkStation.Models;
using Elysium.WorkStation.Services;
using Microsoft.EntityFrameworkCore;
using System.Collections.ObjectModel;
using System.ComponentModel;
#if WINDOWS
using Microsoft.UI.Xaml.Controls;
#endif

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
        private readonly IDbContextFactory<AppDbContext> _dbContextFactory;
        private string _serverUrl;
        private string _sqliteDbPath;
        private string _preparedDbPath = string.Empty;
        private int _fileRetentionHours;
        private int _clipboardRetentionHours;
        private int _notificationRetentionHours;
        private int _kanbanCleanupRetentionDays;
        private int _kanbanCleanupIntervalHours;
        private int _signalRReconnectMinutes;
        private string _selectedTheme = "Light";

        public string ServerUrl
        {
            get => _serverUrl;
            set
            {
                _serverUrl = value;
                OnPropertyChanged();
            }
        }

        public string SqliteDbPath
        {
            get => _sqliteDbPath;
            set
            {
                _sqliteDbPath = value;
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

        public IReadOnlyList<string> ThemeOptions { get; } = ["Light", "Dark"];

        public string SelectedTheme
        {
            get => _selectedTheme;
            set
            {
                if (string.Equals(_selectedTheme, value, StringComparison.Ordinal))
                    return;

                _selectedTheme = value;
                OnPropertyChanged();
                ApplyThemePreview();
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
        public Command ResetPinCommand { get; }
        public Command SelectDbPathCommand { get; }
        public Command UseDefaultDbPathCommand { get; }
        public Command ResetDatabaseCommand { get; }

        public SettingsPage(
            ISettingsService settingsService,
            IStartupService startupService,
            ISecretVaultService secretVaultService,
            IVariableRepository variableRepository,
            IDbContextFactory<AppDbContext> dbContextFactory)
        {
            _settingsService = settingsService;
            _startupService = startupService;
            _secretVaultService = secretVaultService;
            _variableRepository = variableRepository;
            _dbContextFactory = dbContextFactory;
            _serverUrl = settingsService.ServerUrl;
            _sqliteDbPath = settingsService.SqliteDbPath;
            _fileRetentionHours = settingsService.FileRetentionHours;
            _clipboardRetentionHours = settingsService.ClipboardRetentionHours;
            _notificationRetentionHours = settingsService.NotificationRetentionHours;
            _kanbanCleanupRetentionDays = settingsService.KanbanCleanupRetentionDays;
            _kanbanCleanupIntervalHours = settingsService.KanbanCleanupIntervalHours;
            _signalRReconnectMinutes = settingsService.SignalRReconnectMinutes;
            _selectedTheme = settingsService.ThemeMode;
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
                var previousDbPath = DatabasePathProvider.NormalizeOrDefault(_settingsService.SqliteDbPath);
                var nextDbPath = DatabasePathProvider.NormalizeOrDefault(SqliteDbPath);
                var dbPathChanged = !string.Equals(previousDbPath, nextDbPath, StringComparison.OrdinalIgnoreCase);

                if (dbPathChanged)
                {
                    var alreadyPrepared = string.Equals(_preparedDbPath, nextDbPath, StringComparison.OrdinalIgnoreCase);
                    var prepared = alreadyPrepared || await PrepareSelectedDatabasePathAsync(nextDbPath);
                    if (!prepared)
                    {
                        return;
                    }
                }

                _settingsService.ServerUrl = ServerUrl;
                _settingsService.SqliteDbPath = nextDbPath;
                _preparedDbPath = string.Empty;
                _settingsService.FileRetentionHours = FileRetentionHours;
                _settingsService.ClipboardRetentionHours = ClipboardRetentionHours;
                _settingsService.NotificationRetentionHours = NotificationRetentionHours;
                _settingsService.KanbanCleanupRetentionDays = KanbanCleanupRetentionDays;
                _settingsService.KanbanCleanupIntervalHours = KanbanCleanupIntervalHours;
                _settingsService.SignalRReconnectMinutes = SignalRReconnectMinutes;
                _settingsService.ThemeMode = SelectedTheme;
                ApplyThemePreview();

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

                ShowFeedback(
                    dbPathChanged
                        ? "Configuracion guardada. La nueva ruta de DB ya quedo activa."
                        : "Configuracion guardada correctamente.",
                    Color.FromArgb("#1B5E20"));
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

            SelectDbPathCommand = new Command(async () =>
            {
                var pickedPath = await PickSqlitePathAsync(SqliteDbPath);
                if (string.IsNullOrWhiteSpace(pickedPath))
                {
                    return;
                }

                var normalized = DatabasePathProvider.NormalizeOrDefault(pickedPath);
                var prepared = await PrepareSelectedDatabasePathAsync(normalized);
                if (!prepared)
                {
                    return;
                }

                SqliteDbPath = normalized;
                _preparedDbPath = normalized;
            });

            UseDefaultDbPathCommand = new Command(() =>
            {
                SqliteDbPath = DatabasePathProvider.DefaultPath;
                _preparedDbPath = string.Empty;
                ShowFeedback("Ruta de DB restablecida a la ruta por defecto.", Color.FromArgb("#1565C0"));
            });

            ResetDatabaseCommand = new Command(async () =>
            {
                var activeDbPath = DatabasePathProvider.NormalizeOrDefault(_settingsService.SqliteDbPath);
                var dbExists = File.Exists(activeDbPath);

                bool createFromScratch;
                if (dbExists)
                {
                    var choice = await DisplayActionSheet(
                        "Base de datos existente detectada",
                        "Cancelar",
                        null,
                        "Usar informacion existente",
                        "Crear desde cero");

                    if (choice == "Usar informacion existente")
                    {
                        try
                        {
                            await using var existingDb = await _dbContextFactory.CreateDbContextAsync();
                            await existingDb.Database.EnsureCreatedAsync();
                            DatabaseInitializer.Initialize(existingDb);
                            ShowFeedback("Se uso la base existente sin borrar informacion.", Color.FromArgb("#1565C0"));
                        }
                        catch (Exception ex)
                        {
                            await DisplayAlert("DB", $"No se pudo abrir la base existente: {ex.Message}", "OK");
                        }

                        return;
                    }

                    if (choice != "Crear desde cero")
                    {
                        return;
                    }

                    createFromScratch = true;
                }
                else
                {
                    var confirmCreate = await DisplayAlert(
                        "Crear base de datos",
                        "No existe una base en la ruta configurada. Deseas crear una nueva desde cero?",
                        "Crear",
                        "Cancelar");

                    if (!confirmCreate)
                    {
                        return;
                    }

                    createFromScratch = true;
                }

                try
                {
                    await using var db = await _dbContextFactory.CreateDbContextAsync();
                    if (createFromScratch)
                    {
                        await db.Database.EnsureDeletedAsync();
                    }
                    await db.Database.EnsureCreatedAsync();
                    DatabaseInitializer.Initialize(db);
                    ShowFeedback("Base de datos reseteada correctamente.", Color.FromArgb("#1B5E20"));
                }
                catch (Exception ex)
                {
                    await DisplayAlert("DB", $"No se pudo resetear la base de datos: {ex.Message}", "OK");
                }
            });

            InitializeComponent();
            BindingContext = this;

#if WINDOWS
            Loaded += (_, _) => ApplyWindowsThemePickerStyling();
            ThemePicker.HandlerChanged += (_, _) => ApplyWindowsThemePickerStyling();
            if (Application.Current is not null)
            {
                Application.Current.RequestedThemeChanged += (_, _) => ApplyWindowsThemePickerStyling();
            }
#endif
        }

        private static bool IsValidUrl(string url) =>
            !string.IsNullOrWhiteSpace(url) &&
            Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
            (uri.Scheme == "http" || uri.Scheme == "https");

        private void ApplyThemePreview()
        {
            if (Application.Current is null)
                return;

            Application.Current.UserAppTheme = ResolveThemeMode(SelectedTheme);
#if WINDOWS
            ApplyWindowsThemePickerStyling();
#endif
        }

        private static AppTheme ResolveThemeMode(string mode) =>
            string.Equals(mode, "Dark", StringComparison.OrdinalIgnoreCase)
                ? AppTheme.Dark
                : AppTheme.Light;

        private async Task<bool> PrepareSelectedDatabasePathAsync(string targetDbPath)
        {
            var directory = Path.GetDirectoryName(targetDbPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            if (!File.Exists(targetDbPath))
            {
                return await EnsureDatabaseAtPathAsync(targetDbPath, createFromScratch: false);
            }

            var choice = await DisplayActionSheet(
                "La nueva ruta ya tiene una DB",
                "Cancelar",
                null,
                "Usar DB existente",
                "Crear DB desde cero");

            if (choice == "Usar DB existente")
            {
                return await EnsureDatabaseAtPathAsync(targetDbPath, createFromScratch: false);
            }

            if (choice == "Crear DB desde cero")
            {
                return await EnsureDatabaseAtPathAsync(targetDbPath, createFromScratch: true);
            }

            return false;
        }

        private async Task<bool> EnsureDatabaseAtPathAsync(string dbPath, bool createFromScratch)
        {
            try
            {
                if (createFromScratch && File.Exists(dbPath))
                {
                    File.Delete(dbPath);
                }

                var options = new DbContextOptionsBuilder<AppDbContext>()
                    .UseSqlite($"Data Source={dbPath}")
                    .Options;

                await using var db = new AppDbContext(options);
                await db.Database.EnsureCreatedAsync();
                DatabaseInitializer.Initialize(db);
                return true;
            }
            catch (Exception ex)
            {
                await DisplayAlert("DB", $"No se pudo preparar la base en la nueva ruta: {ex.Message}", "OK");
                return false;
            }
        }

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

#if WINDOWS
        private void ApplyWindowsThemePickerStyling()
        {
            if (ThemePicker?.Handler?.PlatformView is not ComboBox comboBox)
                return;

            var isDark = (Application.Current?.UserAppTheme ?? AppTheme.Light) == AppTheme.Dark;

            var background = isDark
                ? global::Windows.UI.Color.FromArgb(255, 26, 49, 89)
                : global::Windows.UI.Color.FromArgb(255, 231, 239, 252);
            var border = isDark
                ? global::Windows.UI.Color.FromArgb(255, 76, 107, 160)
                : global::Windows.UI.Color.FromArgb(255, 188, 208, 243);
            var foreground = isDark
                ? global::Windows.UI.Color.FromArgb(255, 229, 239, 255)
                : global::Windows.UI.Color.FromArgb(255, 30, 63, 119);
            var pointerOver = isDark
                ? global::Windows.UI.Color.FromArgb(255, 33, 59, 102)
                : global::Windows.UI.Color.FromArgb(255, 218, 230, 249);
            var selected = isDark
                ? global::Windows.UI.Color.FromArgb(255, 43, 74, 125)
                : global::Windows.UI.Color.FromArgb(255, 201, 219, 245);

            comboBox.RequestedTheme = isDark
                ? Microsoft.UI.Xaml.ElementTheme.Dark
                : Microsoft.UI.Xaml.ElementTheme.Light;
            comboBox.Background = new global::Microsoft.UI.Xaml.Media.SolidColorBrush(background);
            comboBox.BorderBrush = new global::Microsoft.UI.Xaml.Media.SolidColorBrush(border);
            comboBox.Foreground = new global::Microsoft.UI.Xaml.Media.SolidColorBrush(foreground);

            comboBox.Resources["ComboBoxDropDownBackground"] = new global::Microsoft.UI.Xaml.Media.SolidColorBrush(background);
            comboBox.Resources["ComboBoxDropDownBorderBrush"] = new global::Microsoft.UI.Xaml.Media.SolidColorBrush(border);
            comboBox.Resources["ComboBoxItemBackground"] = new global::Microsoft.UI.Xaml.Media.SolidColorBrush(background);
            comboBox.Resources["ComboBoxItemForeground"] = new global::Microsoft.UI.Xaml.Media.SolidColorBrush(foreground);
            comboBox.Resources["ComboBoxItemBackgroundPointerOver"] = new global::Microsoft.UI.Xaml.Media.SolidColorBrush(pointerOver);
            comboBox.Resources["ComboBoxItemForegroundPointerOver"] = new global::Microsoft.UI.Xaml.Media.SolidColorBrush(foreground);
            comboBox.Resources["ComboBoxItemBackgroundSelected"] = new global::Microsoft.UI.Xaml.Media.SolidColorBrush(selected);
            comboBox.Resources["ComboBoxItemForegroundSelected"] = new global::Microsoft.UI.Xaml.Media.SolidColorBrush(foreground);
        }

        private static async Task<string> PickSqlitePathAsync(string currentPath)
        {
            var picker = new Windows.Storage.Pickers.FileOpenPicker();
            picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary;
            picker.FileTypeFilter.Add(".db");
            picker.FileTypeFilter.Add(".sqlite");
            picker.FileTypeFilter.Add(".sqlite3");

            var window = Application.Current?.Windows.FirstOrDefault();
            if (window?.Handler?.PlatformView is Microsoft.Maui.MauiWinUIWindow nativeWindow)
            {
                WinRT.Interop.InitializeWithWindow.Initialize(picker, nativeWindow.WindowHandle);
            }

            var file = await picker.PickSingleFileAsync();
            return file?.Path ?? string.Empty;
        }
#else
        private static Task<string> PickSqlitePathAsync(string currentPath)
        {
            return Task.FromResult(string.Empty);
        }
#endif
    }
}


