namespace Elysium.WorkStation
{
    public partial class App : Application
    {
        private const string AppDisplayName = "MyWorkStation";
        private readonly Services.ISettingsService _settingsService;
#if WINDOWS
        private readonly Services.IWebHostService _webHostService;
        private readonly Services.IMouseService   _mouseService;
        private readonly Services.ITrayService    _trayService;
        private readonly AppShell                 _appShell;

        private Microsoft.UI.Xaml.Window _nativeWindow;
        private bool _isReallyExiting;
        private bool _isWindowsTitleBarConfigured;
        private Microsoft.UI.Xaml.Controls.Grid _windowsRootGrid;
        private Microsoft.UI.Xaml.Controls.Grid _windowsTitleBarGrid;
        private Microsoft.UI.Xaml.Controls.TextBlock _windowsTitleText;
        private Microsoft.UI.Xaml.Controls.FontIcon _windowsHamburgerIcon;

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
            var window = new Window(_appShell);

#if WINDOWS
            window.Created += (s, e) => _mouseService.Start(1);

            // Interceptar el cierre nativo para ocultar en vez de cerrar
            window.HandlerChanged += (s, e) =>
            {
                if (window.Handler?.PlatformView is not Microsoft.UI.Xaml.Window nativeWindow) return;
                _nativeWindow = nativeWindow;
                ConfigureWindowsTitleBar(nativeWindow);

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

#if WINDOWS
        private void ConfigureWindowsTitleBar(Microsoft.UI.Xaml.Window nativeWindow)
        {
            if (_isWindowsTitleBarConfigured)
            {
                return;
            }

            if (nativeWindow.Content is not Microsoft.UI.Xaml.FrameworkElement originalContent)
            {
                nativeWindow.DispatcherQueue.TryEnqueue(() => ConfigureWindowsTitleBar(nativeWindow));
                return;
            }

            var rootGrid = new Microsoft.UI.Xaml.Controls.Grid();
            rootGrid.RowDefinitions.Add(new Microsoft.UI.Xaml.Controls.RowDefinition
            {
                Height = new Microsoft.UI.Xaml.GridLength(34)
            });
            rootGrid.RowDefinitions.Add(new Microsoft.UI.Xaml.Controls.RowDefinition
            {
                Height = new Microsoft.UI.Xaml.GridLength(1, Microsoft.UI.Xaml.GridUnitType.Star)
            });

            var titleBarGrid = new Microsoft.UI.Xaml.Controls.Grid();
            titleBarGrid.ColumnDefinitions.Add(new Microsoft.UI.Xaml.Controls.ColumnDefinition
            {
                Width = Microsoft.UI.Xaml.GridLength.Auto
            });
            titleBarGrid.ColumnDefinitions.Add(new Microsoft.UI.Xaml.Controls.ColumnDefinition
            {
                Width = new Microsoft.UI.Xaml.GridLength(1, Microsoft.UI.Xaml.GridUnitType.Star)
            });

            var hamburgerButton = new Microsoft.UI.Xaml.Controls.Button
            {
                Width = 34,
                Height = 28,
                Margin = new Microsoft.UI.Xaml.Thickness(6, 3, 6, 3),
                HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Left,
                VerticalAlignment = Microsoft.UI.Xaml.VerticalAlignment.Center,
                Padding = new Microsoft.UI.Xaml.Thickness(0),
                Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(global::Windows.UI.Color.FromArgb(0, 255, 255, 255)),
                BorderThickness = new Microsoft.UI.Xaml.Thickness(0),
                Content = new Microsoft.UI.Xaml.Controls.FontIcon
                {
                    Glyph = "\uE700",
                    FontSize = 16,
                    Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White)
                }
            };
            _windowsHamburgerIcon = (Microsoft.UI.Xaml.Controls.FontIcon)hamburgerButton.Content;
            hamburgerButton.Click += (_, _) => _appShell.ToggleSidebarCommand.Execute(null);
            Microsoft.UI.Xaml.Controls.Grid.SetColumn(hamburgerButton, 0);

            var dragRegion = new Microsoft.UI.Xaml.Controls.Grid
            {
                Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(global::Windows.UI.Color.FromArgb(0, 255, 255, 255))
            };
            var titleText = new Microsoft.UI.Xaml.Controls.TextBlock
            {
                Text = AppDisplayName,
                Margin = new Microsoft.UI.Xaml.Thickness(0, 0, 0, 1),
                VerticalAlignment = Microsoft.UI.Xaml.VerticalAlignment.Center,
                Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White),
                FontSize = 14,
                IsHitTestVisible = false
            };
            _windowsTitleText = titleText;
            dragRegion.Children.Add(titleText);
            Microsoft.UI.Xaml.Controls.Grid.SetColumn(dragRegion, 1);

            titleBarGrid.Children.Add(hamburgerButton);
            titleBarGrid.Children.Add(dragRegion);

            Microsoft.UI.Xaml.Controls.Grid.SetRow(titleBarGrid, 0);
            Microsoft.UI.Xaml.Controls.Grid.SetRow(originalContent, 1);

            rootGrid.Children.Add(titleBarGrid);
            rootGrid.Children.Add(originalContent);

            nativeWindow.ExtendsContentIntoTitleBar = true;
            nativeWindow.SystemBackdrop = null;
            nativeWindow.Content = rootGrid;
            nativeWindow.SetTitleBar(dragRegion);
            nativeWindow.Activated += (_, _) => UpdateWindowsTitleBarColors(nativeWindow);
            _windowsRootGrid = rootGrid;
            _windowsTitleBarGrid = titleBarGrid;

            var titleBar = nativeWindow.AppWindow.TitleBar;
            titleBar.IconShowOptions = Microsoft.UI.Windowing.IconShowOptions.HideIconAndSystemMenu;
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
            var windowBackground = isDarkTheme
                ? global::Windows.UI.Color.FromArgb(255, 11, 19, 38)
                : global::Windows.UI.Color.FromArgb(255, 242, 247, 255);
            var buttonHoverBackground = isDarkTheme
                ? global::Windows.UI.Color.FromArgb(255, 23, 39, 66)
                : global::Windows.UI.Color.FromArgb(255, 221, 232, 250);
            var buttonPressedBackground = isDarkTheme
                ? global::Windows.UI.Color.FromArgb(255, 34, 56, 90)
                : global::Windows.UI.Color.FromArgb(255, 201, 219, 245);
            var buttonTransparentBackground = global::Windows.UI.Color.FromArgb(0, 255, 255, 255);

            if (_windowsRootGrid is not null)
            {
                _windowsRootGrid.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(windowBackground);
            }

            if (_windowsTitleBarGrid is not null)
            {
                _windowsTitleBarGrid.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(barBackground);
            }

            if (_windowsTitleText is not null)
            {
                _windowsTitleText.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(barForeground);
            }

            if (_windowsHamburgerIcon is not null)
            {
                _windowsHamburgerIcon.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(barForeground);
            }

            var titleBar = nativeWindow.AppWindow.TitleBar;
            titleBar.BackgroundColor = barBackground;
            titleBar.ForegroundColor = barForeground;
            titleBar.InactiveBackgroundColor = barBackground;
            titleBar.InactiveForegroundColor = barForeground;
            titleBar.ButtonBackgroundColor = buttonTransparentBackground;
            titleBar.ButtonInactiveBackgroundColor = buttonTransparentBackground;
            titleBar.ButtonForegroundColor = barForeground;
            titleBar.ButtonInactiveForegroundColor = barForeground;
            titleBar.ButtonHoverBackgroundColor = buttonHoverBackground;
            titleBar.ButtonPressedBackgroundColor = buttonPressedBackground;
            titleBar.ButtonHoverForegroundColor = barForeground;
            titleBar.ButtonPressedForegroundColor = barForeground;
        }

        private void UpdateWindowsWindowTitle(Microsoft.UI.Xaml.Window nativeWindow)
        {
            var sectionTitle = _appShell?.CurrentItem?.CurrentItem?.CurrentItem?.Title;
            var fullTitle = string.IsNullOrWhiteSpace(sectionTitle)
                ? AppDisplayName
                : $"{AppDisplayName} - {sectionTitle}";

            nativeWindow.Title = fullTitle;

            if (_windowsTitleText is not null)
            {
                _windowsTitleText.Text = fullTitle;
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
