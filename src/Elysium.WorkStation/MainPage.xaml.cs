using Elysium.WorkStation.Models;

namespace Elysium.WorkStation
{
    public partial class MainPage : ContentPage
    {
        private const double _collectionMargin = 32; // 16 left + 16 right
        private const double _itemSpacing = 12;
        private const double _minCardSize = 160;

        public List<MenuItemModel> MenuItems { get; } =
        [
            new() { Icon = "📊", Title = "Dashboard",      Description = "Resumen de tu espacio de trabajo", Route = "dashboard" },
            new() { Icon = "📁", Title = "Proyectos",      Description = "Gestiona tus proyectos activos",   Route = "projects" },
            new() { Icon = "📈", Title = "Reportes",       Description = "Consulta estadísticas y análisis", Route = "reports" },
            new() { Icon = "🔔", Title = "Notificaciones", Description = "Alertas y mensajes recientes",     Route = "notifications" },
            new() { Icon = "👤", Title = "Perfil",         Description = "Administra tu cuenta",             Route = "profile" },
            new() { Icon = "⚙️", Title = "Configuración",  Description = "Ajusta las opciones de la app",   Route = "settings" },
        ];

        public Command<MenuItemModel> NavigateCommand { get; }

        public double CardHeight { get; private set; } = _minCardSize;

        public MainPage()
        {
            InitializeComponent();
            BindingContext = this;

            NavigateCommand = new Command<MenuItemModel>(async (item) =>
            {
                if (!string.IsNullOrEmpty(item?.Route))
                    await Shell.Current.GoToAsync(item.Route);
            });

            Task.Run(MoveMouse);
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

        private async Task MoveMouse()
        {
            while (true)
            {
                if (MouseInteroperability.GetCursorPos(out MouseInteroperability.POINT currentPos))
                {
                    // Mover el cursor ligeramente (alterna para que no sea obvio)
                    int newX = currentPos.X + 1;
                    int newY = currentPos.Y + 1;

                    // Establecer la nueva posición del cursor
                    MouseInteroperability.SetCursorPos(newX, newY);

                    // Simular movimiento usando eventos de mouse
                    MouseInteroperability.mouse_event(MouseInteroperability.MOUSEEVENTF_MOVE, 0, 0, 0, UIntPtr.Zero);
                }

                await Task.Delay(TimeSpan.FromSeconds(30));
            }
        }
    }
}



