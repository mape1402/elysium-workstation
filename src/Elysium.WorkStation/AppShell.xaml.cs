namespace Elysium.WorkStation
{
    public partial class AppShell : Shell
    {
        public AppShell(MainPage mainPage)
        {
            InitializeComponent();
            HomeContent.Content = mainPage;
            Routing.RegisterRoute("clipboard-history", typeof(Views.ClipboardHistoryPage));
            Routing.RegisterRoute("notifications",     typeof(Views.NotificationsPage));
            Routing.RegisterRoute("files",             typeof(Views.FilesPage));
            Routing.RegisterRoute("settings",          typeof(Views.SettingsPage));
            Routing.RegisterRoute("notes",             typeof(Views.NotesPage));
            Routing.RegisterRoute("note-editor",      typeof(Views.NoteEditorPage));
            Routing.RegisterRoute("kanban",            typeof(Views.KanbanPage));
            Routing.RegisterRoute("variables",         typeof(Views.VariablesPage));
        }
    }
}
