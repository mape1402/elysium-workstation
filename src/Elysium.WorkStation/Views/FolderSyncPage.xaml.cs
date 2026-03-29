using System.Collections.ObjectModel;
using Elysium.WorkStation.Models;
using Elysium.WorkStation.Services;

namespace Elysium.WorkStation.Views
{
    public partial class FolderSyncPage : ContentPage
    {
        private const double FolderCardWidth = 130;
        private const double FolderCardSpacing = 4;

        private readonly IFolderSyncService _folderSyncService;
        private readonly HashSet<VisualElement> _hoveredCards = [];

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
        public Command<FolderSyncLink> ToggleContinuousCommand { get; }
        public Command<FolderSyncLink> DeleteLinkCommand { get; }
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

            ToggleContinuousCommand = new Command<FolderSyncLink>(async link =>
            {
                if (link is null)
                {
                    return;
                }

                if (!link.IsAccepted)
                {
                    await DisplayAlert("Sincronizacion", "La carpeta aun no esta conectada.", "OK");
                    return;
                }

                try
                {
                    await _folderSyncService.SetContinuousAsync(link.Id, !link.ContinuousSyncEnabled);
                }
                catch (Exception ex)
                {
                    await DisplayAlert("Sincronizacion", ex.Message, "OK");
                }
            });

            DeleteLinkCommand = new Command<FolderSyncLink>(async link =>
            {
                if (link is null)
                {
                    return;
                }

                if (link.ContinuousSyncEnabled)
                {
                    await DisplayAlert("Sincronizacion", "Primero deten la sincronizacion para poder eliminar la carpeta.", "OK");
                    return;
                }

                var confirmed = await DisplayAlert(
                    "Sincronizacion",
                    $"Eliminar '{link.Name}'?",
                    "Eliminar",
                    "Cancelar");
                if (!confirmed)
                {
                    return;
                }

                try
                {
                    await _folderSyncService.DeleteSyncAsync(link.Id);
                }
                catch (Exception ex)
                {
                    await DisplayAlert("Sincronizacion", ex.Message, "OK");
                }
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
            UpdateLinksSpan();
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

        private void OnLinksCollectionSizeChanged(object sender, EventArgs e)
        {
            UpdateLinksSpan();
        }

        private void UpdateLinksSpan()
        {
            if (LinksCollectionView?.ItemsLayout is not GridItemsLayout grid)
            {
                return;
            }

            var width = LinksCollectionView.Width;
            if (width <= 0)
            {
                return;
            }

            var span = (int)Math.Floor((width + FolderCardSpacing) / (FolderCardWidth + FolderCardSpacing));
            span = Math.Max(1, span);

            if (grid.Span != span)
            {
                grid.Span = span;
            }
        }

        private async void OnFolderCardPointerEntered(object sender, PointerEventArgs e)
        {
            if (sender is not VisualElement card)
            {
                return;
            }

            _hoveredCards.Add(card);
            SetDeleteButtonVisibility(card, true);
            await AnimateCardScaleAsync(card, 1.03, 120, Easing.CubicOut);
        }

        private async void OnFolderCardPointerExited(object sender, PointerEventArgs e)
        {
            if (sender is not VisualElement card)
            {
                return;
            }

            _hoveredCards.Remove(card);
            SetDeleteButtonVisibility(card, false);
            await AnimateCardScaleAsync(card, 1.0, 120, Easing.CubicOut);
        }

        private async void OnFolderCardPointerPressed(object sender, PointerEventArgs e)
        {
            if (sender is not VisualElement card)
            {
                return;
            }

            await AnimateCardScaleAsync(card, 0.97, 80, Easing.CubicInOut);
        }

        private async void OnFolderCardPointerReleased(object sender, PointerEventArgs e)
        {
            if (sender is not VisualElement card)
            {
                return;
            }

            var target = _hoveredCards.Contains(card) ? 1.03 : 1.0;
            await AnimateCardScaleAsync(card, target, 100, Easing.CubicOut);
        }

        private static Task AnimateCardScaleAsync(VisualElement card, double scale, uint duration, Easing easing)
        {
            card.CancelAnimations();
            return card.ScaleTo(scale, duration, easing);
        }

        private static void SetDeleteButtonVisibility(VisualElement card, bool isVisible)
        {
            if (card is not Border border || border.Content is not Grid grid)
            {
                return;
            }

            var deleteButton = grid.Children
                .OfType<Button>()
                .FirstOrDefault(button => string.Equals(
                    button.AutomationId,
                    "FolderCardDeleteButton",
                    StringComparison.Ordinal));

            if (deleteButton is not null)
            {
                var canDelete = deleteButton.BindingContext is FolderSyncLink link
                    && !link.ContinuousSyncEnabled;
                deleteButton.IsVisible = isVisible && canDelete;
            }
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
