using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Storage;
using Elysium.WorkStation.Models;
using Elysium.WorkStation.Services;

namespace Elysium.WorkStation
{
    public partial class AppShell : Shell
    {
        private const string SidebarPinnedPreferenceKey = "ui.sidebar.pinned";

        private readonly IRoleService _roleService;
        private bool _isSidebarPinned;

        public string ProfileName { get; } = string.IsNullOrWhiteSpace(Environment.UserName) ? "Usuario" : Environment.UserName;

        public Command ToggleSidebarCommand { get; }

        public string AppModeText => _roleService.CurrentRole switch
        {
            AppRole.Server => "Servidor",
            AppRole.Client => "Cliente",
            _ => "Detectando..."
        };

        public Color AppModeColor => _roleService.CurrentRole switch
        {
            AppRole.Server => Color.FromArgb("#1B5E20"),
            AppRole.Client => Color.FromArgb("#0D47A1"),
            _ => Color.FromArgb("#6E83AA")
        };

        public AppShell(IServiceProvider services, IRoleService roleService)
        {
            _roleService = roleService;
            ToggleSidebarCommand = new Command(ToggleSidebar);

            InitializeComponent();
            BindingContext = this;

            _isSidebarPinned = Preferences.Default.Get(SidebarPinnedPreferenceKey, true);
            ApplySidebarState();

            // Lazy page creation via DI prevents early resource resolution crashes.
            HomeContent.ContentTemplate = new DataTemplate(() => services.GetRequiredService<MainPage>());
            VariablesContent.ContentTemplate = new DataTemplate(() => services.GetRequiredService<Views.VariablesPage>());
            KanbanContent.ContentTemplate = new DataTemplate(() => services.GetRequiredService<Views.KanbanPage>());
            NotesContent.ContentTemplate = new DataTemplate(() => services.GetRequiredService<Views.NotesPage>());
            ClipboardContent.ContentTemplate = new DataTemplate(() => services.GetRequiredService<Views.ClipboardHistoryPage>());
            FilesContent.ContentTemplate = new DataTemplate(() => services.GetRequiredService<Views.FilesPage>());
            NotificationsContent.ContentTemplate = new DataTemplate(() => services.GetRequiredService<Views.NotificationsPage>());
            BrainstormContent.ContentTemplate = new DataTemplate(() => services.GetRequiredService<Views.BrainstormPage>());
            SettingsContent.ContentTemplate = new DataTemplate(() => services.GetRequiredService<Views.SettingsPage>());

            // Additional detail routes used from pages.
            Routing.RegisterRoute("note-editor", typeof(Views.NoteEditorPage));

            _roleService.RoleChanged += (_, _) => MainThread.BeginInvokeOnMainThread(() =>
            {
                OnPropertyChanged(nameof(AppModeText));
                OnPropertyChanged(nameof(AppModeColor));
            });
        }

        private void ToggleSidebar()
        {
            _isSidebarPinned = !_isSidebarPinned;
            Preferences.Default.Set(SidebarPinnedPreferenceKey, _isSidebarPinned);
            ApplySidebarState();
        }

        private void ApplySidebarState()
        {
            FlyoutBehavior = _isSidebarPinned ? FlyoutBehavior.Locked : FlyoutBehavior.Disabled;
            FlyoutIsPresented = _isSidebarPinned;
        }
    }
}
