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
        private readonly Controls.WindowsFlyoutItemAnimations _windowsFlyoutItemAnimations = new();

        private Microsoft.UI.Xaml.Window _nativeWindow;
        private bool _isReallyExiting;
        private bool _isNativeWindowClosed;
        private bool _isWindowsTitleBarConfigured;
        private Microsoft.Maui.Controls.TitleBar _windowsTitleBar;
        private Microsoft.Maui.Controls.Button _windowsHamburgerButton;
        private Microsoft.Maui.Controls.PointerGestureRecognizer _windowsHamburgerPointerRecognizer;
        private bool _isWindowsHamburgerPointerInside;

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
                if (_isWindowsTitleBarConfigured && TryGetOpenNativeWindow(out var nativeWindow))
                {
                    UpdateWindowsTitleBarColors(nativeWindow);
                }
            };

            _appShell.Navigated += (_, _) => MainThread.BeginInvokeOnMainThread(() =>
            {
                if (_isWindowsTitleBarConfigured && TryGetOpenNativeWindow(out var nativeWindow))
                {
                    UpdateWindowsWindowTitle(nativeWindow);
                    RefreshWindowsFlyoutItemAnimations(nativeWindow);
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
                _isNativeWindowClosed = false;
                nativeWindow.Closed -= OnNativeWindowClosed;
                nativeWindow.Closed += OnNativeWindowClosed;
                ConfigureWindowsTitleBar(window, nativeWindow);

#if !DEBUG
                _nativeWindow.AppWindow.Closing += (sender, args) =>
                {
                    if (_isReallyExiting)
                    {
                        return;
                    }

                    args.Cancel = true;
                    HideNativeWindowSafe();
                };
#endif
            };

            // Inicializar el icono de la bandeja del sistema
            _trayService.Initialize(
                onShow: ShowNativeWindowSafe,
                onExit: () =>
                {
                    ExitApplication();
                },
                onQuickNote: () =>
                {
                    ShowNativeWindowSafe();
                    if (Shell.Current is not null)
                    {
                        _ = Shell.Current.GoToAsync("note-editor");
                    }
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
            ConfigureWindowsHamburgerButtonInteractions();
            _windowsHamburgerButton.Clicked += (_, _) => _appShell.ToggleSidebarCommand.Execute(null);

            _windowsTitleBar = new Microsoft.Maui.Controls.TitleBar
            {
                Title = BuildWindowTitle(),
                LeadingContent = _windowsHamburgerButton
            };

            window.TitleBar = _windowsTitleBar;
            nativeWindow.SystemBackdrop = null;
            nativeWindow.Activated -= OnNativeWindowActivated;
            nativeWindow.Activated += OnNativeWindowActivated;

            var nativeTitleBar = nativeWindow.AppWindow.TitleBar;
            nativeTitleBar.IconShowOptions = Microsoft.UI.Windowing.IconShowOptions.HideIconAndSystemMenu;

            UpdateWindowsWindowTitle(nativeWindow);
            UpdateWindowsTitleBarColors(nativeWindow);
            RefreshWindowsFlyoutItemAnimations(nativeWindow);

            _isWindowsTitleBarConfigured = true;
        }

        private void UpdateWindowsTitleBarColors(Microsoft.UI.Xaml.Window nativeWindow)
        {
            if (!IsNativeWindowAvailable(nativeWindow))
            {
                return;
            }

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
            if (!IsNativeWindowAvailable(nativeWindow))
            {
                return;
            }

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

        private void OnNativeWindowActivated(object sender, Microsoft.UI.Xaml.WindowActivatedEventArgs args)
        {
            if (sender is Microsoft.UI.Xaml.Window nativeWindow && IsNativeWindowAvailable(nativeWindow))
            {
                UpdateWindowsTitleBarColors(nativeWindow);
                RefreshWindowsFlyoutItemAnimations(nativeWindow);
            }
        }

        private void OnNativeWindowClosed(object sender, Microsoft.UI.Xaml.WindowEventArgs args)
        {
            if (sender is Microsoft.UI.Xaml.Window nativeWindow)
            {
                nativeWindow.Activated -= OnNativeWindowActivated;
                nativeWindow.Closed -= OnNativeWindowClosed;
            }

            if (_windowsHamburgerButton is not null)
            {
                _windowsHamburgerButton.HandlerChanged -= OnWindowsHamburgerButtonHandlerChanged;
                _windowsHamburgerButton.Pressed -= OnWindowsHamburgerPressed;
                _windowsHamburgerButton.Released -= OnWindowsHamburgerReleased;
            }
            DetachWindowsHamburgerPointerRecognizer();

            _isNativeWindowClosed = true;
            _isWindowsTitleBarConfigured = false;
            _windowsTitleBar = null;
            _windowsHamburgerButton = null;

            if (ReferenceEquals(_nativeWindow, sender))
            {
                _nativeWindow = null;
            }
        }

        private bool TryGetOpenNativeWindow(out Microsoft.UI.Xaml.Window nativeWindow)
        {
            nativeWindow = _nativeWindow;
            return IsNativeWindowAvailable(nativeWindow);
        }

        private bool IsNativeWindowAvailable(Microsoft.UI.Xaml.Window nativeWindow)
        {
            if (nativeWindow is null || _isNativeWindowClosed)
            {
                return false;
            }

            try
            {
                _ = nativeWindow.AppWindow;
                return true;
            }
            catch
            {
                _isNativeWindowClosed = true;
                if (ReferenceEquals(_nativeWindow, nativeWindow))
                {
                    _nativeWindow = null;
                }

                return false;
            }
        }

        private void ShowNativeWindowSafe()
        {
            if (!TryGetOpenNativeWindow(out var nativeWindow))
            {
                return;
            }

            try
            {
                nativeWindow.AppWindow.Show(true);
                RefreshWindowsFlyoutItemAnimations(nativeWindow);
            }
            catch
            {
                _isNativeWindowClosed = true;
            }
        }

        private void HideNativeWindowSafe()
        {
            if (!TryGetOpenNativeWindow(out var nativeWindow))
            {
                return;
            }

            try
            {
                nativeWindow.AppWindow.Hide();
            }
            catch
            {
                _isNativeWindowClosed = true;
            }
        }

        private void RefreshWindowsFlyoutItemAnimations(Microsoft.UI.Xaml.Window nativeWindow)
        {
            if (!IsNativeWindowAvailable(nativeWindow))
            {
                return;
            }

            _windowsFlyoutItemAnimations.AttachFromRoot(nativeWindow.Content);
        }

        private void OnWindowsHamburgerButtonHandlerChanged(object sender, EventArgs args)
        {
            if (sender is Microsoft.Maui.Controls.Button button)
            {
                TryAttachWindowsHamburgerAnimation(button);
            }
        }

        private void ConfigureWindowsHamburgerButtonInteractions()
        {
            if (_windowsHamburgerButton is null)
            {
                return;
            }

            _windowsHamburgerButton.HandlerChanged -= OnWindowsHamburgerButtonHandlerChanged;
            _windowsHamburgerButton.HandlerChanged += OnWindowsHamburgerButtonHandlerChanged;

            _windowsHamburgerButton.Pressed -= OnWindowsHamburgerPressed;
            _windowsHamburgerButton.Released -= OnWindowsHamburgerReleased;
            _windowsHamburgerButton.Pressed += OnWindowsHamburgerPressed;
            _windowsHamburgerButton.Released += OnWindowsHamburgerReleased;

            AttachWindowsHamburgerPointerRecognizer();
            TryAttachWindowsHamburgerAnimation(_windowsHamburgerButton);
        }

        private void AttachWindowsHamburgerPointerRecognizer()
        {
            if (_windowsHamburgerButton is null)
            {
                return;
            }

            DetachWindowsHamburgerPointerRecognizer();

            _windowsHamburgerPointerRecognizer = new Microsoft.Maui.Controls.PointerGestureRecognizer();
            _windowsHamburgerPointerRecognizer.PointerEntered += OnWindowsHamburgerPointerEntered;
            _windowsHamburgerPointerRecognizer.PointerExited += OnWindowsHamburgerPointerExited;
            _windowsHamburgerButton.GestureRecognizers.Add(_windowsHamburgerPointerRecognizer);
        }

        private void DetachWindowsHamburgerPointerRecognizer()
        {
            if (_windowsHamburgerButton is null || _windowsHamburgerPointerRecognizer is null)
            {
                _windowsHamburgerPointerRecognizer = null;
                return;
            }

            _windowsHamburgerPointerRecognizer.PointerEntered -= OnWindowsHamburgerPointerEntered;
            _windowsHamburgerPointerRecognizer.PointerExited -= OnWindowsHamburgerPointerExited;
            _windowsHamburgerButton.GestureRecognizers.Remove(_windowsHamburgerPointerRecognizer);
            _windowsHamburgerPointerRecognizer = null;
        }

        private void OnWindowsHamburgerPressed(object sender, EventArgs args)
        {
            AnimateWindowsHamburger(0.88, 0.85, 1.5, -9, 90, true);
        }

        private void OnWindowsHamburgerReleased(object sender, EventArgs args)
        {
            var hovered = _isWindowsHamburgerPointerInside;
            AnimateWindowsHamburger(
                hovered ? 1.08 : 1.0,
                1.0,
                0,
                0,
                130,
                hovered);
        }

        private void OnWindowsHamburgerPointerEntered(object sender, Microsoft.Maui.Controls.PointerEventArgs args)
        {
            _isWindowsHamburgerPointerInside = true;
            AnimateWindowsHamburger(1.08, 1.0, 0, 0, 130, true);
        }

        private void OnWindowsHamburgerPointerExited(object sender, Microsoft.Maui.Controls.PointerEventArgs args)
        {
            _isWindowsHamburgerPointerInside = false;
            AnimateWindowsHamburger(1.0, 1.0, 0, 0, 130, false);
        }

        private void AnimateWindowsHamburger(double targetScale, double targetOpacity, double targetTranslateY, double targetRotation, uint duration, bool hovered)
        {
            if (_windowsHamburgerButton is null)
            {
                return;
            }

            var effectiveTheme = UserAppTheme == AppTheme.Unspecified
                ? (Current?.RequestedTheme ?? AppTheme.Light)
                : UserAppTheme;
            var hoverColor = effectiveTheme == AppTheme.Dark
                ? Color.FromArgb("#2B4E87")
                : Color.FromArgb("#DCEAFE");

            MainThread.BeginInvokeOnMainThread(async () =>
            {
                if (_windowsHamburgerButton is null)
                {
                    return;
                }

                _windowsHamburgerButton.CancelAnimations();
                _windowsHamburgerButton.BackgroundColor = hovered ? hoverColor : Colors.Transparent;

                await Task.WhenAll(
                    _windowsHamburgerButton.ScaleTo(targetScale, duration, Easing.CubicOut),
                    _windowsHamburgerButton.FadeTo(targetOpacity, duration, Easing.CubicOut),
                    _windowsHamburgerButton.TranslateTo(0, targetTranslateY, duration, Easing.CubicOut),
                    _windowsHamburgerButton.RotateTo(targetRotation, duration, Easing.CubicOut));
            });
        }

        private static void TryAttachWindowsHamburgerAnimation(Microsoft.Maui.Controls.Button button)
        {
            if (button.Handler?.PlatformView is Microsoft.UI.Xaml.Controls.Button platformButton)
            {
                Controls.GlobalButtonAnimations.Attach(platformButton, button);
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
