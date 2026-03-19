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

        public List<MenuItemModel> MenuItems { get; } =
        [
            new() { Icon = "📊", Title = "Dashboard",      Description = "Resumen de tu espacio de trabajo", Route = "dashboard" },
            new() { Icon = "📁", Title = "Proyectos",      Description = "Gestiona tus proyectos activos",   Route = "projects" },
            new() { Icon = "📈", Title = "Reportes",       Description = "Consulta estadísticas y análisis", Route = "reports" },
            new() { Icon = "🔔", Title = "Notificaciones", Description = "Alertas y mensajes recientes",     Route = "notifications" },
            new() { Icon = "👤", Title = "Perfil",         Description = "Administra tu cuenta",             Route = "profile" },
            new() { Icon = "⚙️", Title = "Configuración",  Description = "Ajusta las opciones de la app",   Route = "settings" },
            new() { Icon = "📋", Title = "Portapapeles",    Description = "Historial de textos sincronizados", Route = "clipboard-history" },
        ];

        public Command<MenuItemModel> NavigateCommand { get; }

        public double CardHeight { get; private set; } = _minCardSize;

        public string RoleStatusText => _roleService.CurrentRole switch
        {
            AppRole.Server => "🟢  Servidor activo · http://localhost:5050",
            AppRole.Client => "🔵  Modo cliente",
            _              => "⚪  Detectando rol..."
        };

        public Color RoleStatusColor => _roleService.CurrentRole switch
        {
            AppRole.Server => Color.FromArgb("#1B5E20"),
            AppRole.Client => Color.FromArgb("#0D47A1"),
            _              => Color.FromArgb("#424242")
        };

        public MainPage(IRoleService roleService, IClipboardSyncService clipboardSyncService)
        {
            _roleService = roleService;
            _clipboardSyncService = clipboardSyncService;

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
            if (_roleService.CurrentRole != AppRole.Undetermined) return;

            bool serverRunning = await _roleService.IsServerRunningAsync();
            if (serverRunning)
            {
                _roleService.SetClientRole();
                await _clipboardSyncService.StartAsync("http://localhost:5050/hubs/workstation");
                return;
            }

            bool becomeServer = await DisplayAlert(
                "Rol de instancia",
                "No se detectó un servidor activo. ¿Deseas iniciar esta instancia como servidor?",
                "Sí, iniciar servidor",
                "No, modo cliente");

            if (becomeServer)
                await _roleService.ActivateServerAsync();
            else
                _roleService.SetClientRole();

            await _clipboardSyncService.StartAsync("http://localhost:5050/hubs/workstation");
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
    }
}



