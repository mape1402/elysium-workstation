using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Globalization;
using Elysium.WorkStation.Models;
using Elysium.WorkStation.Services;

namespace Elysium.WorkStation
{
    public partial class MainPage : ContentPage
    {
        private readonly IRoleService _roleService;
        private readonly IClipboardSyncService _clipboardSyncService;
        private readonly ISettingsService _settingsService;
        private readonly IFileTransferService _fileTransferService;
        private readonly IFolderSyncService _folderSyncService;
        private readonly ICleanupService _cleanupService;
        private readonly IKanbanCleanupService _kanbanCleanupService;
        private readonly HashSet<VisualElement> _hoveredQuickCards = [];
        private bool _isQuickActionNavigating;

        public ObservableCollection<HomeQuickActionItem> QuickActions { get; } = [];

        public Command OpenVariablesCommand { get; }
        public Command OpenKanbanCommand { get; }
        public Command OpenNotesCommand { get; }

        public string WelcomeTitle => $"Workspace de {ResolveDisplayName()}";
        public string WelcomeSubtitle => "Organiza tu flujo con accesos rapidos, estado en tiempo real y sincronizacion centralizada.";
        public string CurrentDateText => DateTime.Now.ToString("dddd, dd 'de' MMMM", CultureInfo.GetCultureInfo("es-MX"));

        public string RoleStatusText => _roleService.CurrentRole switch
        {
            AppRole.Server => "Servidor activo",
            AppRole.Client => "Modo cliente",
            _ => "Inicializando"
        };

        public Color RoleStatusColor => _roleService.CurrentRole switch
        {
            AppRole.Server => Color.FromArgb("#4EDB88"),
            AppRole.Client => Color.FromArgb("#7DB5FF"),
            _ => Color.FromArgb("#CFD8E8")
        };

        public string ServerSummaryText => _roleService.CurrentRole switch
        {
            AppRole.Server => $"Hub local: {_settingsService.HubUrl}",
            AppRole.Client => $"Servidor: {_settingsService.ServerUrl}",
            _ => "Servidor: pendiente"
        };

        public string ClipboardSummaryText => $"{_clipboardSyncService.History.Count} registros";
        public string FileTransferSummaryText => $"{_fileTransferService.History.Count} transferencias";
        public string FolderSyncSummaryText => $"{_folderSyncService.Links.Count} carpetas";

        public string ClipboardConnectionText => BuildConnectionText(_clipboardSyncService.IsConnected);
        public string FileTransferConnectionText => BuildConnectionText(_fileTransferService.IsConnected);
        public string FolderSyncConnectionText => BuildConnectionText(_folderSyncService.IsConnected);

        public Color ClipboardStatusColor => BuildConnectionColor(_clipboardSyncService.IsConnected);
        public Color FileTransferStatusColor => BuildConnectionColor(_fileTransferService.IsConnected);
        public Color FolderSyncStatusColor => BuildConnectionColor(_folderSyncService.IsConnected);

        public MainPage(
            IRoleService roleService,
            IClipboardSyncService clipboardSyncService,
            ISettingsService settingsService,
            IFileTransferService fileTransferService,
            IFolderSyncService folderSyncService,
            ICleanupService cleanupService,
            IKanbanCleanupService kanbanCleanupService)
        {
            _roleService = roleService;
            _clipboardSyncService = clipboardSyncService;
            _settingsService = settingsService;
            _fileTransferService = fileTransferService;
            _folderSyncService = folderSyncService;
            _cleanupService = cleanupService;
            _kanbanCleanupService = kanbanCleanupService;

            OpenVariablesCommand = new Command(async () => await NavigateToRouteAsync("//variables-root"));
            OpenKanbanCommand = new Command(async () => await NavigateToRouteAsync("//kanban-root"));
            OpenNotesCommand = new Command(async () => await NavigateToRouteAsync("//notes-root"));

            foreach (var quickAction in CreateQuickActions())
            {
                QuickActions.Add(quickAction);
            }

            _roleService.RoleChanged += (_, _) => MainThread.BeginInvokeOnMainThread(RefreshDashboardBindings);
            _clipboardSyncService.ConnectionStateChanged += (_, _) => MainThread.BeginInvokeOnMainThread(RefreshDashboardBindings);
            _fileTransferService.ConnectionStateChanged += (_, _) => MainThread.BeginInvokeOnMainThread(RefreshDashboardBindings);
            _folderSyncService.StateChanged += (_, _) => MainThread.BeginInvokeOnMainThread(RefreshDashboardBindings);
            _clipboardSyncService.History.CollectionChanged += OnTrackedCollectionChanged;
            _fileTransferService.History.CollectionChanged += OnTrackedCollectionChanged;
            _folderSyncService.Links.CollectionChanged += OnTrackedCollectionChanged;

            InitializeComponent();
            BindingContext = this;
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();

            // On Windows, DisplayAlert uses ContentDialog and requires XamlRoot.
            if (!IsLoaded)
            {
                var tcs = new TaskCompletionSource();
                void handler(object s, EventArgs e)
                {
                    Loaded -= handler;
                    tcs.TrySetResult();
                }

                Loaded += handler;
                await tcs.Task;
            }

            bool settingsMissing()
            {
                return !_settingsService.IsConfigured;
            }

#if DEBUG
            if (_roleService.CurrentRole == AppRole.Undetermined)
            {
                bool runAsServer = await DisplayAlert(
                    "Rol de instancia",
                    "Selecciona como iniciar esta instancia en DEBUG.",
                    "Servidor",
                    "Cliente");

                if (runAsServer)
                {
                    // En DEBUG debemos definir primero el scope de preferencias
                    // para leer la configuracion correcta de server.
                    PreferenceScopeProvider.SetDebugRole(AppRole.Server);
                    if (settingsMissing())
                    {
                        await Shell.Current.GoToAsync("//settings-root");
                        return;
                    }

                    await _roleService.ActivateServerAsync();
                }
                else
                {
                    _roleService.SetClientRole();
                }
            }

            if (settingsMissing())
            {
                await Shell.Current.GoToAsync("//settings-root");
                return;
            }
#else
            if (settingsMissing())
            {
                await Shell.Current.GoToAsync("//settings-root");
                return;
            }

            if (_roleService.CurrentRole == AppRole.Undetermined)
            {
                bool serverRunning = await _roleService.IsServerRunningAsync();
                if (serverRunning)
                {
                    _roleService.SetClientRole();
                }
                else
                {
                    bool becomeServer = await DisplayAlert(
                        "Rol de instancia",
                        "No se detecto un servidor activo. Deseas iniciar esta instancia como servidor?",
                        "Si, iniciar servidor",
                        "No, modo cliente");

                    if (becomeServer)
                    {
                        await _roleService.ActivateServerAsync();
                    }
                    else
                    {
                        _roleService.SetClientRole();
                    }
                }
            }
#endif

            string hubUrl = _roleService.CurrentRole == AppRole.Server
                ? $"http://localhost:{_settingsService.ServerPort}/hubs/workstation"
                : _settingsService.HubUrl;

            await _clipboardSyncService.StartAsync(hubUrl);
            await _fileTransferService.StartAsync(hubUrl);
            await _folderSyncService.StartAsync(hubUrl);
            await _cleanupService.StartAsync();
            await _kanbanCleanupService.StartAsync();

            RefreshDashboardBindings();
        }

        private async void OnQuickActionTapped(object sender, TappedEventArgs e)
        {
            if (sender is not BindableObject bindable
                || bindable.BindingContext is not HomeQuickActionItem action
                || string.IsNullOrWhiteSpace(action.Route))
            {
                return;
            }

            await NavigateToRouteAsync(action.Route);
        }

        private async void OnQuickCardPointerEntered(object sender, PointerEventArgs e)
        {
            if (sender is not VisualElement card)
            {
                return;
            }

            _hoveredQuickCards.Add(card);
            await AnimateQuickCardAsync(card, 1.02, -2, 110);
        }

        private async void OnQuickCardPointerExited(object sender, PointerEventArgs e)
        {
            if (sender is not VisualElement card)
            {
                return;
            }

            _hoveredQuickCards.Remove(card);
            await AnimateQuickCardAsync(card, 1.0, 0, 110);
        }

        private async void OnQuickCardPointerPressed(object sender, PointerEventArgs e)
        {
            if (sender is not VisualElement card)
            {
                return;
            }

            await AnimateQuickCardAsync(card, 0.985, 0, 80);
        }

        private async void OnQuickCardPointerReleased(object sender, PointerEventArgs e)
        {
            if (sender is not VisualElement card)
            {
                return;
            }

            var targetScale = _hoveredQuickCards.Contains(card) ? 1.02 : 1.0;
            var targetTranslate = _hoveredQuickCards.Contains(card) ? -2 : 0;
            await AnimateQuickCardAsync(card, targetScale, targetTranslate, 100);
        }

        private void OnTrackedCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            MainThread.BeginInvokeOnMainThread(RefreshDashboardBindings);
        }

        private async Task NavigateToRouteAsync(string route)
        {
            if (_isQuickActionNavigating || string.IsNullOrWhiteSpace(route))
            {
                return;
            }

            _isQuickActionNavigating = true;
            try
            {
                await Shell.Current.GoToAsync(route);
            }
            finally
            {
                _isQuickActionNavigating = false;
            }
        }

        private void RefreshDashboardBindings()
        {
            OnPropertyChanged(nameof(WelcomeTitle));
            OnPropertyChanged(nameof(WelcomeSubtitle));
            OnPropertyChanged(nameof(CurrentDateText));
            OnPropertyChanged(nameof(RoleStatusText));
            OnPropertyChanged(nameof(RoleStatusColor));
            OnPropertyChanged(nameof(ServerSummaryText));
            OnPropertyChanged(nameof(ClipboardSummaryText));
            OnPropertyChanged(nameof(FileTransferSummaryText));
            OnPropertyChanged(nameof(FolderSyncSummaryText));
            OnPropertyChanged(nameof(ClipboardConnectionText));
            OnPropertyChanged(nameof(FileTransferConnectionText));
            OnPropertyChanged(nameof(FolderSyncConnectionText));
            OnPropertyChanged(nameof(ClipboardStatusColor));
            OnPropertyChanged(nameof(FileTransferStatusColor));
            OnPropertyChanged(nameof(FolderSyncStatusColor));
        }

        private IEnumerable<HomeQuickActionItem> CreateQuickActions()
        {
            yield return new HomeQuickActionItem(
                "Notificaciones",
                "Actividad y alertas recientes.",
                "//notifications-root",
                "\uE7F4",
                Color.FromArgb("#1C66E0"),
                Color.FromArgb("#DCE9FF"));

            yield return new HomeQuickActionItem(
                "Kanban",
                "Organiza tareas por columnas.",
                "//kanban-root",
                "\uE8A5",
                Color.FromArgb("#2460D8"),
                Color.FromArgb("#DEE9FF"));

            yield return new HomeQuickActionItem(
                "Brainstorming",
                "Mapas de ideas y topics.",
                "//brainstorm-root",
                "\uEC1F",
                Color.FromArgb("#2A5FD0"),
                Color.FromArgb("#E0EBFF"));

            yield return new HomeQuickActionItem(
                "Notas",
                "Captura notas rapidas.",
                "//notes-root",
                "\uE70B",
                Color.FromArgb("#2A63D5"),
                Color.FromArgb("#E1ECFF"));

            yield return new HomeQuickActionItem(
                "Portapapeles",
                "Historial sincronizado.",
                "//clipboard-root",
                "\uE8C8",
                Color.FromArgb("#2A62CF"),
                Color.FromArgb("#E4EEFF"));

            yield return new HomeQuickActionItem(
                "Archivos",
                "Comparte y recibe archivos.",
                "//files-root",
                "\uE8B7",
                Color.FromArgb("#2C63CC"),
                Color.FromArgb("#E3EDFF"));

            yield return new HomeQuickActionItem(
                "Sync carpeta",
                "Gestiona carpetas conectadas.",
                "//folder-sync-root",
                "\uE895",
                Color.FromArgb("#2F68D7"),
                Color.FromArgb("#DFEAFF"));

            yield return new HomeQuickActionItem(
                "Variables",
                "Variables seguras y PIN.",
                "//variables-root",
                "\uE72E",
                Color.FromArgb("#255EC9"),
                Color.FromArgb("#E3ECFF"));

            yield return new HomeQuickActionItem(
                "Configuracion",
                "Conexion, tema y base de datos.",
                "//settings-root",
                "\uE713",
                Color.FromArgb("#245CC4"),
                Color.FromArgb("#E1EAFF"));
        }

        private string ResolveDisplayName()
        {
            var firstName = _settingsService.ProfileFirstName?.Trim();
            if (!string.IsNullOrWhiteSpace(firstName))
            {
                return firstName;
            }

            return "Usuario";
        }

        private static string BuildConnectionText(bool isConnected)
        {
            return isConnected ? "Conectado" : "Sin conexion";
        }

        private static Color BuildConnectionColor(bool isConnected)
        {
            return isConnected
                ? Color.FromArgb("#38C976")
                : Color.FromArgb("#F06161");
        }

        private static Task AnimateQuickCardAsync(VisualElement card, double scale, double translateY, uint duration)
        {
            card.CancelAnimations();
            return Task.WhenAll(
                card.ScaleTo(scale, duration, Easing.CubicOut),
                card.TranslateTo(0, translateY, duration, Easing.CubicOut));
        }
    }
}
