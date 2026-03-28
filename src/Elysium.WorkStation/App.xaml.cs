namespace Elysium.WorkStation
{
    public partial class App : Application
    {
        private readonly Services.ISettingsService _settingsService;
#if WINDOWS
        private readonly Services.IWebHostService _webHostService;
        private readonly Services.IMouseService   _mouseService;
        private readonly Services.ITrayService    _trayService;
        private readonly AppShell                 _appShell;

        private Microsoft.UI.Xaml.Window _nativeWindow;
        private bool _isReallyExiting;

        public App(AppShell                  appShell,
                   Services.ISettingsService settingsService,
                   Services.IWebHostService  webHostService,
                   Services.IMouseService    mouseService,
                   Services.ITrayService     trayService)
        {
            _appShell       = appShell;
            _settingsService = settingsService;
            _webHostService = webHostService;
            _mouseService   = mouseService;
            _trayService    = trayService;
            InitializeComponent();
            UserAppTheme = ResolveTheme(_settingsService.ThemeMode);
        }
#else
        private readonly AppShell _appShell;

        public App(AppShell appShell, Services.ISettingsService settingsService)
        {
            _appShell = appShell;
            _settingsService = settingsService;
            InitializeComponent();
            UserAppTheme = ResolveTheme(_settingsService.ThemeMode);
        }
#endif

        protected override Window CreateWindow(IActivationState activationState)
        {
            var window = new Window(_appShell);

#if WINDOWS
            window.Created += (s, e) => _mouseService.Start(1);

            // Interceptar el cierre nativo para ocultar en vez de cerrar
            window.HandlerChanged += (s, e) =>
            {
                if (window.Handler?.PlatformView is not Microsoft.UI.Xaml.Window nativeWindow) return;
                _nativeWindow = nativeWindow;

#if !DEBUG
                _nativeWindow.AppWindow.Closing += (sender, args) =>
                {
                    if (_isReallyExiting) return;
                    args.Cancel = true;
                    _nativeWindow.AppWindow.Hide();
                };
#endif
            };

            // Inicializar el icono de la bandeja del sistema
            _trayService.Initialize(
                onShow: () => _nativeWindow?.AppWindow.Show(true),
                onExit: () =>
                {
                    ExitApplication();
                },
                onQuickNote: () =>
                {
                    _nativeWindow?.AppWindow.Show(true);
                    _ = Shell.Current.GoToAsync("note-editor");
                }
            );
#endif

            return window;
        }

        private static AppTheme ResolveTheme(string mode) =>
            string.Equals(mode, "Dark", StringComparison.OrdinalIgnoreCase)
                ? AppTheme.Dark
                : AppTheme.Light;

#if WINDOWS
        private void ExitApplication()
        {
            if (_isReallyExiting) return;
            _isReallyExiting = true;
            _trayService.Dispose();
            _mouseService.Stop();
            _ = _webHostService.StopAsync();
            Application.Current?.Quit();
        }
#endif
    }
}
