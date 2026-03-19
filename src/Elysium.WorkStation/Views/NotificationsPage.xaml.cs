using Elysium.WorkStation.Models;
using Elysium.WorkStation.Services;
using System.Collections.ObjectModel;

namespace Elysium.WorkStation.Views
{
    public partial class NotificationsPage : ContentPage
    {
        private readonly INotificationRepository _repository;

        public ObservableCollection<NotificationEntry> Notifications { get; } = [];

        public string CountText => Notifications.Count switch
        {
            0 => "Sin notificaciones",
            1 => "1 notificación",
            _ => $"{Notifications.Count} notificaciones"
        };

        public Command ClearCommand { get; }

        public NotificationsPage(INotificationRepository repository)
        {
            _repository = repository;

            ClearCommand = new Command(async () =>
            {
                await _repository.DeleteAllAsync();
                Notifications.Clear();
                OnPropertyChanged(nameof(CountText));
            });

            InitializeComponent();
            BindingContext = this;
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();
            await LoadAsync();
        }

        private async Task LoadAsync()
        {
            var items = await _repository.GetAllAsync();
            Notifications.Clear();
            foreach (var item in items)
                Notifications.Add(item);
            OnPropertyChanged(nameof(CountText));
        }
    }
}
