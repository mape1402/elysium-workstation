namespace Elysium.WorkStation
{
    public partial class App : Application
    {
#if WINDOWS
        private readonly Services.IWebHostService _webHostService;
        private readonly Services.IMouseService   _mouseService;
        private readonly Services.ITrayService    _trayService;

        private Microsoft.UI.Xaml.Window _nativeWindow;
        private bool _isReallyExiting;

        public App(Services.IWebHostService webHostService,
                   Services.IMouseService   mouseService,
                   Services.ITrayService    trayService)
        {
            _webHostService = webHostService;
            _mouseService   = mouseService;
            _trayService    = trayService;
            InitializeComponent();
        }
#else
        public App()
        {
            InitializeComponent();
        }
#endif

        protected override Window CreateWindow(IActivationState activationState)
        {
            var window = new Window(new AppShell());

#if WINDOWS
            // Iniciar servicios al crear la ventana
            window.Created += async (s, e) =>
            {
                await _webHostService.StartAsync();
                _mouseService.Start(1);
            };

            // Interceptar el cierre nativo para ocultar en vez de cerrar
            window.HandlerChanged += (s, e) =>
            {
                if (window.Handler?.PlatformView is not Microsoft.UI.Xaml.Window nativeWindow) return;
                _nativeWindow = nativeWindow;

                _nativeWindow.AppWindow.Closing += (sender, args) =>
                {
                    if (_isReallyExiting) return;
                    args.Cancel = true;
                    _nativeWindow.AppWindow.Hide();
                };
            };

            // Inicializar el icono de la bandeja del sistema
            _trayService.Initialize(
                onShow: () => _nativeWindow?.AppWindow.Show(true),
                onExit: () =>
                {
                    _isReallyExiting = true;
                    _trayService.Dispose();
                    _mouseService.Stop();
                    _ = _webHostService.StopAsync();
                    Application.Current?.Quit();
                }
            );
#endif

            return window;
        }
    }
}
