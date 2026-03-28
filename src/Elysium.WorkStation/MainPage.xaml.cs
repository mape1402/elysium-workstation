using Elysium.WorkStation.Services;
using Elysium.WorkStation.Models;

namespace Elysium.WorkStation
{
    public partial class MainPage : ContentPage
    {
        private readonly IRoleService _roleService;
        private readonly IClipboardSyncService _clipboardSyncService;
        private readonly ISettingsService _settingsService;
        private readonly IFileTransferService _fileTransferService;
        private readonly ICleanupService _cleanupService;
        private readonly IKanbanCleanupService _kanbanCleanupService;

        public Command OpenVariablesCommand { get; }
        public Command OpenKanbanCommand { get; }
        public Command OpenNotesCommand { get; }

        public string RoleStatusText => _roleService.CurrentRole switch
        {
            AppRole.Server => $"🟢  Servidor activo · {_settingsService.ServerUrl}",
            AppRole.Client => "🔵  Modo cliente",
            _ => "⚪  Detectando rol..."
        };

        public Color RoleStatusColor => _roleService.CurrentRole switch
        {
            AppRole.Server => Color.FromArgb("#1B5E20"),
            AppRole.Client => Color.FromArgb("#0D47A1"),
            _ => Color.FromArgb("#424242")
        };

        public MainPage(
            IRoleService roleService,
            IClipboardSyncService clipboardSyncService,
            ISettingsService settingsService,
            IFileTransferService fileTransferService,
            ICleanupService cleanupService,
            IKanbanCleanupService kanbanCleanupService)
        {
            _roleService = roleService;
            _clipboardSyncService = clipboardSyncService;
            _settingsService = settingsService;
            _fileTransferService = fileTransferService;
            _cleanupService = cleanupService;
            _kanbanCleanupService = kanbanCleanupService;

            OpenVariablesCommand = new Command(async () => await Shell.Current.GoToAsync("//variables-root"));
            OpenKanbanCommand = new Command(async () => await Shell.Current.GoToAsync("//kanban-root"));
            OpenNotesCommand = new Command(async () => await Shell.Current.GoToAsync("//notes-root"));

            _roleService.RoleChanged += (_, _) => MainThread.BeginInvokeOnMainThread(() =>
            {
                OnPropertyChanged(nameof(RoleStatusText));
                OnPropertyChanged(nameof(RoleStatusColor));
            });

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

            if (!_settingsService.IsConfigured)
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
                        await _roleService.ActivateServerAsync();
                    else
                        _roleService.SetClientRole();
                }
            }

            string hubUrl = _roleService.CurrentRole == AppRole.Server
                ? $"http://localhost:{_settingsService.ServerPort}/hubs/workstation"
                : _settingsService.HubUrl;

            await _clipboardSyncService.StartAsync(hubUrl);
            await _fileTransferService.StartAsync(hubUrl);
            await _cleanupService.StartAsync();
            await _kanbanCleanupService.StartAsync();
        }
    }
}

