using Elysium.WorkStation.Models;
using Elysium.WorkStation.Services;
using System.Collections.ObjectModel;

namespace Elysium.WorkStation.Views
{
    public partial class NotificationsPage : ContentPage
    {
        private readonly INotificationRepository _repository;
        private readonly IFolderSyncService _folderSyncService;

        public ObservableCollection<NotificationEntry> Notifications { get; } = [];

        public string CountText => Notifications.Count switch
        {
            0 => "Sin notificaciones",
            1 => "1 notificación",
            _ => $"{Notifications.Count} notificaciones"
        };

        public Command ClearCommand { get; }
        public Command<NotificationEntry> AcceptFolderSyncInviteCommand { get; }

        public NotificationsPage(
            INotificationRepository repository,
            IFolderSyncService folderSyncService)
        {
            _repository = repository;
            _folderSyncService = folderSyncService;

            ClearCommand = new Command(async () =>
            {
                await _repository.DeleteAllAsync();
                Notifications.Clear();
                OnPropertyChanged(nameof(CountText));
            });

            AcceptFolderSyncInviteCommand = new Command<NotificationEntry>(async entry =>
            {
                if (entry is null)
                {
                    return;
                }

                if (!entry.TryGetFolderSyncInvitePayload(out var payload))
                {
                    await DisplayAlert("Notificaciones", "La invitacion no contiene informacion valida.", "OK");
                    return;
                }

                var folderPath = await PickFolderAsync();
                if (string.IsNullOrWhiteSpace(folderPath))
                {
                    return;
                }

                try
                {
                    var invite = new FolderSyncInvite
                    {
                        InviteId = payload.InviteId,
                        SyncId = payload.SyncId,
                        Name = payload.Name,
                        Description = payload.Description,
                        IgnorePathsJson = string.IsNullOrWhiteSpace(payload.IgnorePathsJson) ? "[]" : payload.IgnorePathsJson,
                        RequesterClientId = payload.RequesterClientId,
                        RequesterName = payload.RequesterName,
                        RequesterFolderPath = payload.RequesterFolderPath
                    };

                    await _folderSyncService.AcceptInviteAsync(invite, folderPath);
                    await _repository.DeleteByIdAsync(entry.Id);
                    Notifications.Remove(entry);
                    OnPropertyChanged(nameof(CountText));
                }
                catch (Exception ex)
                {
                    await DisplayAlert("Notificaciones", ex.Message, "OK");
                }
            });

            InitializeComponent();
            BindingContext = this;
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();
            _folderSyncService.StateChanged -= OnFolderSyncStateChanged;
            _folderSyncService.StateChanged += OnFolderSyncStateChanged;
            await LoadAsync();
        }

        protected override void OnDisappearing()
        {
            base.OnDisappearing();
            _folderSyncService.StateChanged -= OnFolderSyncStateChanged;
        }

        private async Task LoadAsync()
        {
            var items = await _repository.GetAllAsync();
            Notifications.Clear();
            foreach (var item in items)
                Notifications.Add(item);
            OnPropertyChanged(nameof(CountText));
        }

        private void OnFolderSyncStateChanged(object sender, EventArgs e)
        {
            MainThread.BeginInvokeOnMainThread(async () => await LoadAsync());
        }

        private async Task<string> PickFolderAsync()
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
    }
}
