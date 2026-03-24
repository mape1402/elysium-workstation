using Elysium.WorkStation.Models;
using Elysium.WorkStation.Services;

namespace Elysium.WorkStation
{
    public partial class MainPage : ContentPage
    {
        private const double _collectionMargin = 32; // 16 left + 16 right
        private const double _itemSpacing = 12;
        private const double _minCardSize = 160;

        private readonly IRoleService _roleService;
        private readonly IClipboardSyncService _clipboardSyncService;
        private readonly ISettingsService _settingsService;
        private readonly IFileTransferService _fileTransferService;
        private readonly ICleanupService _cleanupService;

        public List<MenuItemModel> MenuItems { get; } =
        [
            new() { Icon = "📊", Title = "Dashboard",      Description = "Resumen de tu espacio de trabajo", Route = "dashboard" },
            new() { Icon = "📁", Title = "Proyectos",      Description = "Gestiona tus proyectos activos",   Route = "projects" },
            new() { Icon = "📈", Title = "Reportes",       Description = "Consulta estadísticas y análisis", Route = "reports" },
            new() { Icon = "🔔", Title = "Notificaciones", Description = "Alertas y mensajes recientes",     Route = "notifications" },
            new() { Icon = "👤", Title = "Perfil",         Description = "Administra tu cuenta",             Route = "profile" },
            new() { Icon = "⚙️", Title = "Configuración",  Description = "Ajusta las opciones de la app",   Route = "settings" },
            new() { Icon = "📋", Title = "Portapapeles",    Description = "Historial de textos sincronizados", Route = "clipboard-history" },
            new() { Icon = "📂", Title = "Archivos",         Description = "Env\u00eda y recibe archivos en la red",  Route = "files" },
            new() { Icon = "📝", Title = "Notas rápidas",     Description = "Crea notas tipo post-it",                Route = "notes" },
            new() { Icon = "📌", Title = "Kanban",             Description = "Tablero de tareas con estatus",          Route = "kanban" },
        ];

        public Command<MenuItemModel> NavigateCommand { get; }

        public double CardHeight { get; private set; } = _minCardSize;

        public string RoleStatusText => _roleService.CurrentRole switch
        {
            AppRole.Server => $"🟢  Servidor activo · {_settingsService.ServerUrl}",
            AppRole.Client => "🔵  Modo cliente",
            _              => "⚪  Detectando rol..."
        };

        public Color RoleStatusColor => _roleService.CurrentRole switch
        {
            AppRole.Server => Color.FromArgb("#1B5E20"),
            AppRole.Client => Color.FromArgb("#0D47A1"),
            _              => Color.FromArgb("#424242")
        };

        public MainPage(IRoleService roleService, IClipboardSyncService clipboardSyncService, ISettingsService settingsService, IFileTransferService fileTransferService, ICleanupService cleanupService)
        {
            _roleService = roleService;
            _clipboardSyncService = clipboardSyncService;
            _settingsService = settingsService;
            _fileTransferService = fileTransferService;
            _cleanupService = cleanupService;

            NavigateCommand = new Command<MenuItemModel>(async (item) =>
            {
                if (!string.IsNullOrEmpty(item?.Route))
                    await Shell.Current.GoToAsync(item.Route);
            });

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

            // On Windows, DisplayAlert uses ContentDialog which requires XamlRoot.
            // XamlRoot is only available once the page is fully in the visual tree.
            // OnAppearing fires before Loaded on Windows, so we must wait for it.
            if (!IsLoaded)
            {
                var tcs = new TaskCompletionSource();
                void handler(object s, EventArgs e) { Loaded -= handler; tcs.TrySetResult(); }
                Loaded += handler;
                await tcs.Task;
            }

            if (!_settingsService.IsConfigured)
            {
                await Shell.Current.GoToAsync("settings");
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
                        "No se detectó un servidor activo. ¿Deseas iniciar esta instancia como servidor?",
                        "Sí, iniciar servidor",
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
        }

        protected override void OnSizeAllocated(double width, double height)
        {
            base.OnSizeAllocated(width, height);
            UpdateCardLayout(width);
        }

        private void UpdateCardLayout(double width)
        {
            if (width <= 0) return;

            double available = width - _collectionMargin;
            int span = Math.Max(2, (int)((available + _itemSpacing) / (_minCardSize + _itemSpacing)));
            double cardSize = (available - (span - 1) * _itemSpacing) / span;

            if (MenuCollectionView.ItemsLayout is GridItemsLayout grid && grid.Span != span)
                grid.Span = span;

            if (Math.Abs(CardHeight - cardSize) > 0.5)
            {
                CardHeight = cardSize;
                OnPropertyChanged(nameof(CardHeight));
            }
        }

        private async void OnCardPointerEntered(object sender, PointerEventArgs e)
        {
            if (sender is View view)
            {
                await view.TranslateTo(0, -6, 150, Easing.CubicOut);
            }
        }

        private async void OnCardPointerExited(object sender, PointerEventArgs e)
        {
            if (sender is View view)
            {
                await view.TranslateTo(0, 0, 150, Easing.CubicOut);
            }
        }
    }
}



