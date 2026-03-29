using System.Collections.ObjectModel;
using Elysium.WorkStation.Models;
using Elysium.WorkStation.Services;

namespace Elysium.WorkStation.Views
{
    public partial class FolderSyncPage : ContentPage
    {
        private readonly IFolderSyncService _folderSyncService;

        public ObservableCollection<FolderSyncLink> Links => _folderSyncService.Links;
        public ObservableCollection<FolderSyncInvite> PendingInvites => _folderSyncService.PendingInvites;

        public string StatusText => _folderSyncService.IsConnected
            ? "🟢  Conectado al canal de sincronizacion"
            : "🔴  Sin conexion";

        public Color StatusColor => _folderSyncService.IsConnected
            ? Color.FromArgb("#1B5E20")
            : Color.FromArgb("#B71C1C");

        public bool HasPendingInvites => PendingInvites.Count > 0;

        public Command AddSyncCommand { get; }
        public Command<FolderSyncLink> OpenDetailCommand { get; }
        public Command<FolderSyncInvite> AcceptInviteCommand { get; }
        public Command<FolderSyncInvite> RejectInviteCommand { get; }

        public FolderSyncPage(IFolderSyncService folderSyncService)
        {
            _folderSyncService = folderSyncService;

            AddSyncCommand = new Command(async () =>
            {
                var editor = new FolderSyncEditorPage();
                await Navigation.PushModalAsync(editor);
                var result = await editor.ResultTask;
                if (result is null)
                {
                    return;
                }

                try
                {
                    var created = await _folderSyncService.CreateSyncRequestAsync(
                        result.Name,
                        result.Description,
                        result.FolderPath,
                        []);

                    await NavigateToDetailAsync(created.Id);
                }
                catch (Exception ex)
                {
                    await DisplayAlert("Sincronizacion", ex.Message, "OK");
                }
            });

            OpenDetailCommand = new Command<FolderSyncLink>(async link =>
            {
                if (link is null)
                {
                    return;
                }

                await NavigateToDetailAsync(link.Id);
            });

            AcceptInviteCommand = new Command<FolderSyncInvite>(async invite =>
            {
                if (invite is null) return;

                var folderPath = await PickFolderAsync();
                if (string.IsNullOrWhiteSpace(folderPath))
                {
                    return;
                }

                try
                {
                    var accepted = await _folderSyncService.AcceptInviteAsync(invite, folderPath);
                    await NavigateToDetailAsync(accepted.Id);
                }
                catch (Exception ex)
                {
                    await DisplayAlert("Sincronizacion", ex.Message, "OK");
                }
            });

            RejectInviteCommand = new Command<FolderSyncInvite>(async invite =>
            {
                if (invite is null) return;
                await _folderSyncService.RejectInviteAsync(invite);
            });

            InitializeComponent();
            BindingContext = this;
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();
            _folderSyncService.StateChanged -= OnServiceStateChanged;
            _folderSyncService.StateChanged += OnServiceStateChanged;
            PendingInvites.CollectionChanged -= PendingInvitesCollectionChanged;
            PendingInvites.CollectionChanged += PendingInvitesCollectionChanged;
            await _folderSyncService.ReloadAsync();
            RefreshBindings();
        }

        protected override void OnDisappearing()
        {
            _folderSyncService.StateChanged -= OnServiceStateChanged;
            PendingInvites.CollectionChanged -= PendingInvitesCollectionChanged;
            base.OnDisappearing();
        }

        private void OnServiceStateChanged(object sender, EventArgs e)
        {
            MainThread.BeginInvokeOnMainThread(RefreshBindings);
        }

        private void RefreshBindings()
        {
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(StatusColor));
            OnPropertyChanged(nameof(HasPendingInvites));
        }

        private void PendingInvitesCollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            OnPropertyChanged(nameof(HasPendingInvites));
        }

        private static async Task<string> PickFolderAsync()
        {
#if WINDOWS
            var picker = new Windows.Storage.Pickers.FolderPicker();
            picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary;
            picker.FileTypeFilter.Add("*");

            var window = Application.Current?.Windows.FirstOrDefault();
            if (window?.Handler?.PlatformView is Microsoft.Maui.MauiWinUIWindow nativeWindow)
            {
                WinRT.Interop.InitializeWithWindow.Initialize(picker, nativeWindow.WindowHandle);
            }

            var folder = await picker.PickSingleFolderAsync();
            return folder?.Path ?? string.Empty;
#else
            return string.Empty;
#endif
        }

        private static Task NavigateToDetailAsync(int linkId)
        {
            return Shell.Current.GoToAsync($"folder-sync-detail?id={linkId}");
        }
    }
}
