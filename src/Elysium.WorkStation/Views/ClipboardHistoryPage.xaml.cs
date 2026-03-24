using Elysium.WorkStation.Models;
using Elysium.WorkStation.Services;
using System.Collections.ObjectModel;

namespace Elysium.WorkStation.Views
{
    public partial class ClipboardHistoryPage : ContentPage
    {
        private readonly IClipboardSyncService _clipboardSyncService;
        private readonly IToastService _toastService;

        public ObservableCollection<ClipboardEntry> History => _clipboardSyncService.History;

        public Command<ClipboardEntry> CopyCommand { get; }
        public Command SendCommand { get; }

        public string StatusText => _clipboardSyncService.IsConnected
            ? "🟢  Sincronización activa"
            : "🔴  Sin conexión al servidor";

        public Color StatusColor => _clipboardSyncService.IsConnected
            ? Color.FromArgb("#1B5E20")
            : Color.FromArgb("#B71C1C");

        public ClipboardHistoryPage(IClipboardSyncService clipboardSyncService, IToastService toastService)
        {
            _clipboardSyncService = clipboardSyncService;
            _toastService = toastService;

            CopyCommand = new Command<ClipboardEntry>(async (entry) =>
            {
                if (entry is null) return;
                await Clipboard.Default.SetTextAsync(entry.Text);
                await _toastService.ShowAsync("📋 Copiado al portapapeles");
            });

            SendCommand = new Command(async () =>
                await _clipboardSyncService.SendCurrentClipboardAsync());

            InitializeComponent();
            BindingContext = this;

            _clipboardSyncService.ConnectionStateChanged += (_, _) =>
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    OnPropertyChanged(nameof(StatusText));
                    OnPropertyChanged(nameof(StatusColor));
                });
        }
    }
}
