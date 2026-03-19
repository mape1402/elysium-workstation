namespace Elysium.WorkStation
{
    public partial class AppShell : Shell
    {
        public AppShell(MainPage mainPage)
        {
            InitializeComponent();
            HomeContent.Content = mainPage;
            Routing.RegisterRoute("clipboard-history", typeof(Views.ClipboardHistoryPage));
        }
    }
}
