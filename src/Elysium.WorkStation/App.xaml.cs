namespace Elysium.WorkStation
{
    public partial class App : Application
    {
        private const string AppDisplayName = "MyWorkStation";
        private readonly Services.ISettingsService _settingsService;
#if WINDOWS
        private readonly Services.IWebHostService _webHostService;
        private readonly Services.IMouseService _mouseService;
        private readonly Services.ITrayService _trayService;
        private readonly AppShell _appShell;

        private Microsoft.UI.Xaml.Window _nativeWindow;
        private bool _isReallyExiting;
        private bool _isWindowsTitleBarConfigured;
        private Microsoft.Maui.Controls.TitleBar _windowsTitleBar;
        private Microsoft.Maui.Controls.Button _windowsHamburgerButton;

        public App(
            AppShell appShell,
            Services.ISettingsService settingsService,
            Services.IWebHostService webHostService,
            Services.IMouseService mouseService,
            Services.ITrayService trayService)
        {
            _appShell = appShell;
            _settingsService = settingsService;
            _webHostService = webHostService;
            _mouseService = mouseService;
            _trayService = trayService;

            InitializeComponent();
            UserAppTheme = ResolveTheme(_settingsService.ThemeMode);

            RequestedThemeChanged += (_, _) =>
            {
                if (_nativeWindow is not null && _isWindowsTitleBarConfigured)
                {
                    UpdateWindowsTitleBarColors(_nativeWindow);
                }
            };

            _appShell.Navigated += (_, _) => MainThread.BeginInvokeOnMainThread(() =>
            {
                if (_nativeWindow is not null && _isWindowsTitleBarConfigured)
                {
                    UpdateWindowsWindowTitle(_nativeWindow);
                }
            });
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
            var window = new Window(_appShell)
            {
                Title = AppDisplayName
            };

#if WINDOWS
            window.Created += (s, e) => _mouseService.Start(1);

            // Interceptar el cierre nativo para ocultar en vez de cerrar
            window.HandlerChanged += (s, e) =>
            {
                if (window.Handler?.PlatformView is not Microsoft.UI.Xaml.Window nativeWindow)
                {
                    return;
                }

                _nativeWindow = nativeWindow;
                ConfigureWindowsTitleBar(window, nativeWindow);

#if !DEBUG
                _nativeWindow.AppWindow.Closing += (sender, args) =>
                {
                    if (_isReallyExiting)
                    {
                        return;
                    }

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

#if WINDOWS
        private void ConfigureWindowsTitleBar(Window window, Microsoft.UI.Xaml.Window nativeWindow)
        {
            if (_isWindowsTitleBarConfigured)
            {
                return;
            }

            _windowsHamburgerButton = new Microsoft.Maui.Controls.Button
            {
                Text = "\u2630",
                FontSize = 16,
                WidthRequest = 34,
                HeightRequest = 30,
                Padding = new Thickness(0),
                Margin = new Thickness(6, 0, 6, 0),
                HorizontalOptions = LayoutOptions.Start,
                VerticalOptions = LayoutOptions.Center,
                CornerRadius = 6,
                BorderWidth = 0,
                BackgroundColor = Colors.Transparent
            };
            _windowsHamburgerButton.Clicked += (_, _) => _appShell.ToggleSidebarCommand.Execute(null);

            _windowsTitleBar = new Microsoft.Maui.Controls.TitleBar
            {
                Title = BuildWindowTitle(),
                LeadingContent = _windowsHamburgerButton
            };

            window.TitleBar = _windowsTitleBar;
            nativeWindow.SystemBackdrop = null;
            nativeWindow.Activated += (_, _) => UpdateWindowsTitleBarColors(nativeWindow);

            var nativeTitleBar = nativeWindow.AppWindow.TitleBar;
            nativeTitleBar.IconShowOptions = Microsoft.UI.Windowing.IconShowOptions.HideIconAndSystemMenu;

            UpdateWindowsWindowTitle(nativeWindow);
            UpdateWindowsTitleBarColors(nativeWindow);

            _isWindowsTitleBarConfigured = true;
        }

        private void UpdateWindowsTitleBarColors(Microsoft.UI.Xaml.Window nativeWindow)
        {
            var effectiveTheme = UserAppTheme == AppTheme.Unspecified
                ? (Current?.RequestedTheme ?? AppTheme.Light)
                : UserAppTheme;
            var isDarkTheme = effectiveTheme == AppTheme.Dark;

            var barBackground = isDarkTheme
                ? global::Windows.UI.Color.FromArgb(255, 8, 21, 39)
                : global::Windows.UI.Color.FromArgb(255, 242, 247, 255);
            var barForeground = isDarkTheme
                ? global::Windows.UI.Color.FromArgb(255, 255, 255, 255)
                : global::Windows.UI.Color.FromArgb(255, 30, 63, 119);
            var buttonHoverBackground = isDarkTheme
                ? global::Windows.UI.Color.FromArgb(255, 23, 39, 66)
                : global::Windows.UI.Color.FromArgb(255, 221, 232, 250);
            var buttonPressedBackground = isDarkTheme
                ? global::Windows.UI.Color.FromArgb(255, 34, 56, 90)
                : global::Windows.UI.Color.FromArgb(255, 201, 219, 245);
            var buttonTransparentBackground = global::Windows.UI.Color.FromArgb(0, 255, 255, 255);

            if (_windowsTitleBar is not null)
            {
                _windowsTitleBar.BackgroundColor = Color.FromArgb(isDarkTheme ? "#081527" : "#F2F7FF");
                _windowsTitleBar.ForegroundColor = Color.FromArgb(isDarkTheme ? "#FFFFFF" : "#1E3F77");
            }

            if (_windowsHamburgerButton is not null)
            {
                _windowsHamburgerButton.BackgroundColor = Colors.Transparent;
                _windowsHamburgerButton.TextColor = Color.FromArgb(isDarkTheme ? "#FFFFFF" : "#1E3F77");
            }

            var nativeTitleBar = nativeWindow.AppWindow.TitleBar;
            nativeTitleBar.BackgroundColor = barBackground;
            nativeTitleBar.ForegroundColor = barForeground;
            nativeTitleBar.InactiveBackgroundColor = barBackground;
            nativeTitleBar.InactiveForegroundColor = barForeground;
            nativeTitleBar.ButtonBackgroundColor = buttonTransparentBackground;
            nativeTitleBar.ButtonInactiveBackgroundColor = buttonTransparentBackground;
            nativeTitleBar.ButtonForegroundColor = barForeground;
            nativeTitleBar.ButtonInactiveForegroundColor = barForeground;
            nativeTitleBar.ButtonHoverBackgroundColor = buttonHoverBackground;
            nativeTitleBar.ButtonPressedBackgroundColor = buttonPressedBackground;
            nativeTitleBar.ButtonHoverForegroundColor = barForeground;
            nativeTitleBar.ButtonPressedForegroundColor = barForeground;

            UpdateWindowsWindowBackground(nativeWindow, isDarkTheme);
        }

        private void UpdateWindowsWindowTitle(Microsoft.UI.Xaml.Window nativeWindow)
        {
            var fullTitle = BuildWindowTitle();
            nativeWindow.Title = fullTitle;

            if (_windowsTitleBar is not null)
            {
                _windowsTitleBar.Title = fullTitle;
            }
        }

        private string BuildWindowTitle()
        {
            var sectionTitle = _appShell?.CurrentItem?.CurrentItem?.CurrentItem?.Title;
            return string.IsNullOrWhiteSpace(sectionTitle)
                ? AppDisplayName
                : $"{AppDisplayName} - {sectionTitle}";
        }

        private static void UpdateWindowsWindowBackground(Microsoft.UI.Xaml.Window nativeWindow, bool isDarkTheme)
        {
            var windowBackground = isDarkTheme
                ? global::Windows.UI.Color.FromArgb(255, 14, 24, 48)
                : global::Windows.UI.Color.FromArgb(255, 247, 250, 255);
            var brush = new Microsoft.UI.Xaml.Media.SolidColorBrush(windowBackground);

            if (nativeWindow.Content is Microsoft.UI.Xaml.Controls.Panel panel)
            {
                panel.Background = brush;
                return;
            }

            if (nativeWindow.Content is Microsoft.UI.Xaml.Controls.Control control)
            {
                control.Background = brush;
            }
        }
#endif

        private static AppTheme ResolveTheme(string mode) =>
            string.Equals(mode, "Dark", StringComparison.OrdinalIgnoreCase)
                ? AppTheme.Dark
                : AppTheme.Light;

#if WINDOWS
        private void ExitApplication()
        {
            if (_isReallyExiting)
            {
                return;
            }

            _isReallyExiting = true;
            _trayService.Dispose();
            _mouseService.Stop();
            _ = _webHostService.StopAsync();
            Application.Current?.Quit();
        }
#endif
    }
}
