using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Storage;
using Elysium.WorkStation.Models;
using Elysium.WorkStation.Services;
using Elysium.WorkStation.Views;

namespace Elysium.WorkStation
{
    public partial class AppShell : Shell
    {
        private const string SidebarPinnedPreferenceKey = "ui.sidebar.pinned";
        private const string DefaultProfileImageSource = "dotnet_bot.png";

        private readonly IRoleService _roleService;
        private readonly ISettingsService _settingsService;
        private bool _isSidebarPinned;
        private bool _hasProfilePromptedOnStartup;
        private bool _isProfileEditorOpen;
        private string _profileName = "Usuario";
        private string _profilePhotoSource = DefaultProfileImageSource;

        public string ProfileName
        {
            get => _profileName;
            private set
            {
                if (string.Equals(_profileName, value, StringComparison.Ordinal))
                {
                    return;
                }

                _profileName = value;
                OnPropertyChanged();
            }
        }

        public string ProfilePhotoSource
        {
            get => _profilePhotoSource;
            private set
            {
                if (string.Equals(_profilePhotoSource, value, StringComparison.Ordinal))
                {
                    return;
                }

                _profilePhotoSource = value;
                OnPropertyChanged();
            }
        }

        public Command ToggleSidebarCommand { get; }
        public Command EditProfileCommand { get; }

        public string AppModeText => _roleService.CurrentRole switch
        {
            AppRole.Server => "Servidor",
            AppRole.Client => "Cliente",
            _ => "Detectando..."
        };

        public string AppProductVersionText => $"MyWorkStation v{AppInfo.Current.VersionString}";
        public string AppPoweredByText => "Powered by Elysium Coding";

        public Color AppModeColor => _roleService.CurrentRole switch
        {
            AppRole.Server => Color.FromArgb("#1B5E20"),
            AppRole.Client => Color.FromArgb("#0D47A1"),
            _ => Color.FromArgb("#6E83AA")
        };

        public AppShell(IServiceProvider services, IRoleService roleService, ISettingsService settingsService)
        {
            _roleService = roleService;
            _settingsService = settingsService;
            ToggleSidebarCommand = new Command(ToggleSidebar);
            EditProfileCommand = new Command(async () => await EditProfileAsync(false));

            InitializeComponent();
            BindingContext = this;

            _isSidebarPinned = ScopedPreferences.Get(SidebarPinnedPreferenceKey, true);
            ApplySidebarState();
            LoadProfileFromSettings();
            Loaded += OnShellLoaded;

            // Lazy page creation via DI prevents early resource resolution crashes.
            HomeContent.ContentTemplate = new DataTemplate(() => services.GetRequiredService<MainPage>());
            VariablesContent.ContentTemplate = new DataTemplate(() => services.GetRequiredService<Views.VariablesPage>());
            KanbanContent.ContentTemplate = new DataTemplate(() => services.GetRequiredService<Views.KanbanPage>());
            NotesContent.ContentTemplate = new DataTemplate(() => services.GetRequiredService<Views.NotesPage>());
            ClipboardContent.ContentTemplate = new DataTemplate(() => services.GetRequiredService<Views.ClipboardHistoryPage>());
            FilesContent.ContentTemplate = new DataTemplate(() => services.GetRequiredService<Views.FilesPage>());
            FolderSyncContent.ContentTemplate = new DataTemplate(() => services.GetRequiredService<Views.FolderSyncPage>());
            NotificationsContent.ContentTemplate = new DataTemplate(() => services.GetRequiredService<Views.NotificationsPage>());
            BrainstormContent.ContentTemplate = new DataTemplate(() => services.GetRequiredService<Views.BrainstormPage>());
            SettingsContent.ContentTemplate = new DataTemplate(() => services.GetRequiredService<Views.SettingsPage>());

            // Additional detail routes used from pages.
            Routing.RegisterRoute("note-editor", typeof(Views.NoteEditorPage));
            Routing.RegisterRoute("folder-sync-detail", typeof(Views.FolderSyncDetailPage));

            _roleService.RoleChanged += (_, _) => MainThread.BeginInvokeOnMainThread(() =>
            {
                OnPropertyChanged(nameof(AppModeText));
                OnPropertyChanged(nameof(AppModeColor));
                OnPropertyChanged(nameof(AppProductVersionText));
                OnPropertyChanged(nameof(AppPoweredByText));
            });
        }

        private void ToggleSidebar()
        {
            _isSidebarPinned = !_isSidebarPinned;
            ScopedPreferences.Set(SidebarPinnedPreferenceKey, _isSidebarPinned);
            ApplySidebarState();
        }

        private void ApplySidebarState()
        {
            FlyoutBehavior = _isSidebarPinned ? FlyoutBehavior.Locked : FlyoutBehavior.Disabled;
            FlyoutIsPresented = _isSidebarPinned;
        }

        private async void OnShellLoaded(object sender, EventArgs e)
        {
            if (_hasProfilePromptedOnStartup)
            {
                return;
            }

            _hasProfilePromptedOnStartup = true;
            Loaded -= OnShellLoaded;

            if (_settingsService.ProfileIsRegistered)
            {
                return;
            }

            await EditProfileAsync(true);
        }

        private async Task EditProfileAsync(bool isStartupPrompt)
        {
            if (_isProfileEditorOpen)
            {
                return;
            }

            _isProfileEditorOpen = true;
            try
            {
                var editor = new ProfileEditorPage(
                    _settingsService.ProfileFirstName,
                    _settingsService.ProfileLastName,
                    _settingsService.ProfilePhotoPath,
                    isStartupPrompt);

                await Navigation.PushModalAsync(editor);
                var result = await editor.ResultTask;
                if (result is null)
                {
                    return;
                }

                _settingsService.ProfileFirstName = result.FirstName;
                _settingsService.ProfileLastName = result.LastName;
                _settingsService.ProfilePhotoPath = result.PhotoPath ?? string.Empty;
                _settingsService.ProfileIsRegistered = true;

                LoadProfileFromSettings();
            }
            finally
            {
                _isProfileEditorOpen = false;
            }
        }

        private void LoadProfileFromSettings()
        {
            ProfileName = BuildProfileName(_settingsService.ProfileFirstName, _settingsService.ProfileLastName);
            ProfilePhotoSource = ResolveProfilePhotoSource(_settingsService.ProfilePhotoPath);
        }

        private static string BuildProfileName(string firstName, string lastName)
        {
            var fullName = $"{firstName ?? string.Empty} {lastName ?? string.Empty}".Trim();
            if (!string.IsNullOrWhiteSpace(fullName))
            {
                return fullName;
            }

            return "Usuario";
        }

        private static string ResolveProfilePhotoSource(string photoPath)
        {
            if (!string.IsNullOrWhiteSpace(photoPath) && File.Exists(photoPath))
            {
                return photoPath;
            }

            return DefaultProfileImageSource;
        }
    }
}
