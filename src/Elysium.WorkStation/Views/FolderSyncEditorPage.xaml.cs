namespace Elysium.WorkStation.Views
{
    public sealed record FolderSyncEditorResult(string Name, string Description, string FolderPath);

    public partial class FolderSyncEditorPage : ContentPage
    {
        private readonly TaskCompletionSource<FolderSyncEditorResult?> _resultSource = new();
        private bool _isClosing;

        public Task<FolderSyncEditorResult?> ResultTask => _resultSource.Task;

        public FolderSyncEditorPage()
        {
            InitializeComponent();
            BindingContext = this;
        }

        protected override void OnDisappearing()
        {
            if (!_isClosing && !_resultSource.Task.IsCompleted)
            {
                _resultSource.TrySetResult(null);
            }

            base.OnDisappearing();
        }

        private async void OnPickFolderClicked(object sender, EventArgs e)
        {
            var path = await PickFolderAsync();
            if (!string.IsNullOrWhiteSpace(path))
            {
                FolderPathEntry.Text = path;
            }
        }

        private async void OnCancelClicked(object sender, EventArgs e)
        {
            await CompleteAndCloseAsync(null);
        }

        private async void OnSaveClicked(object sender, EventArgs e)
        {
            var name = NameEntry.Text?.Trim() ?? string.Empty;
            var description = DescriptionEntry.Text?.Trim() ?? string.Empty;
            var folderPath = FolderPathEntry.Text?.Trim() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(name))
            {
                await DisplayAlert("Sincronizacion", "Debes indicar un nombre.", "OK");
                return;
            }

            if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
            {
                await DisplayAlert("Sincronizacion", "Debes seleccionar una carpeta valida.", "OK");
                return;
            }

            await CompleteAndCloseAsync(new FolderSyncEditorResult(name, description, folderPath));
        }

        private async Task<string> PickFolderAsync()
        {
#if WINDOWS
            var picker = new Windows.Storage.Pickers.FolderPicker();
            picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary;
            picker.FileTypeFilter.Add("*");

            var hwnd = ((Microsoft.Maui.MauiWinUIWindow)Window.Handler.PlatformView).WindowHandle;
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            var folder = await picker.PickSingleFolderAsync();
            return folder?.Path ?? string.Empty;
#else
            await DisplayAlert("Sincronizacion", "Seleccion de carpeta disponible en Windows.", "OK");
            return string.Empty;
#endif
        }

        private async Task CompleteAndCloseAsync(FolderSyncEditorResult? result)
        {
            if (_isClosing)
            {
                return;
            }

            _isClosing = true;
            try
            {
                await CloseModalAsync();
                _resultSource.TrySetResult(result);
            }
            finally
            {
                _isClosing = false;
            }
        }

        private async Task CloseModalAsync()
        {
            var navigation =
                Shell.Current?.Navigation
                ?? Application.Current?.Windows.FirstOrDefault()?.Page?.Navigation
                ?? Navigation;

            if (navigation?.ModalStack is null || navigation.ModalStack.Count == 0)
            {
                return;
            }

            if (!navigation.ModalStack.Contains(this))
            {
                return;
            }

            await navigation.PopModalAsync();
        }
    }
}
