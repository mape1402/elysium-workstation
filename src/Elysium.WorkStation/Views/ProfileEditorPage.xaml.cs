using Microsoft.Maui.Storage;

namespace Elysium.WorkStation.Views
{
    public sealed record ProfileEditorResult(string FirstName, string LastName, string PhotoPath);

    public partial class ProfileEditorPage : ContentPage
    {
        private const string DefaultProfileImageSource = "dotnet_bot.png";

        private readonly bool _isStartupPrompt;
        private readonly TaskCompletionSource<ProfileEditorResult?> _resultSource = new();
        private string _photoPath;
        private string _photoPreviewSource = DefaultProfileImageSource;
        private bool _hasPhoto;

        public string PageTitle => "Editar perfil";
        public string HeaderText => _isStartupPrompt
            ? "Completa tu perfil para comenzar"
            : "Actualiza tu perfil";
        public string SaveButtonText => "Guardar";
        public string CancelButtonText => _isStartupPrompt ? "Omitir ahora" : "Cancelar";
        public bool HasPhoto
        {
            get => _hasPhoto;
            private set
            {
                if (_hasPhoto == value)
                {
                    return;
                }

                _hasPhoto = value;
                OnPropertyChanged();
            }
        }

        public string PhotoPreviewSource
        {
            get => _photoPreviewSource;
            private set
            {
                if (string.Equals(_photoPreviewSource, value, StringComparison.Ordinal))
                {
                    return;
                }

                _photoPreviewSource = value;
                OnPropertyChanged();
            }
        }

        public Task<ProfileEditorResult?> ResultTask => _resultSource.Task;

        public ProfileEditorPage(string firstName, string lastName, string photoPath, bool isStartupPrompt)
        {
            _isStartupPrompt = isStartupPrompt;
            _photoPath = photoPath ?? string.Empty;

            InitializeComponent();
            BindingContext = this;

            FirstNameEntry.Text = firstName ?? string.Empty;
            LastNameEntry.Text = lastName ?? string.Empty;
            ApplyPhotoState();
        }

        protected override void OnDisappearing()
        {
            if (!_resultSource.Task.IsCompleted)
            {
                _resultSource.TrySetResult(null);
            }

            base.OnDisappearing();
        }

        private async void OnSelectPhotoClicked(object sender, EventArgs e)
        {
            try
            {
                var fileResult = await FilePicker.Default.PickAsync(new PickOptions
                {
                    PickerTitle = "Selecciona una foto de perfil",
                    FileTypes = FilePickerFileType.Images
                });

                if (fileResult is null)
                {
                    return;
                }

                var resolvedPath = await ResolvePickedPhotoPathAsync(fileResult);
                if (string.IsNullOrWhiteSpace(resolvedPath))
                {
                    await DisplayAlert("Perfil", "No se pudo cargar la foto seleccionada.", "OK");
                    return;
                }

                _photoPath = resolvedPath;
                ApplyPhotoState();
            }
            catch (Exception ex)
            {
                await DisplayAlert("Perfil", $"No se pudo seleccionar la foto: {ex.Message}", "OK");
            }
        }

        private void OnClearPhotoClicked(object sender, EventArgs e)
        {
            _photoPath = string.Empty;
            ApplyPhotoState();
        }

        private async void OnCancelClicked(object sender, EventArgs e)
        {
            _resultSource.TrySetResult(null);
            await CloseModalAsync();
        }

        private async void OnSaveClicked(object sender, EventArgs e)
        {
            var firstName = FirstNameEntry.Text?.Trim() ?? string.Empty;
            var lastName = LastNameEntry.Text?.Trim() ?? string.Empty;

            _resultSource.TrySetResult(new ProfileEditorResult(firstName, lastName, _photoPath));
            await CloseModalAsync();
        }

        private void ApplyPhotoState()
        {
            if (!string.IsNullOrWhiteSpace(_photoPath) && File.Exists(_photoPath))
            {
                PhotoPreviewSource = _photoPath;
                HasPhoto = true;
                return;
            }

            PhotoPreviewSource = DefaultProfileImageSource;
            HasPhoto = false;
        }

        private static async Task<string> ResolvePickedPhotoPathAsync(FileResult fileResult)
        {
            if (!string.IsNullOrWhiteSpace(fileResult.FullPath) && File.Exists(fileResult.FullPath))
            {
                return fileResult.FullPath;
            }

            var extension = Path.GetExtension(fileResult.FileName);
            if (string.IsNullOrWhiteSpace(extension))
            {
                extension = ".png";
            }

            var destinationPath = Path.Combine(
                FileSystem.Current.AppDataDirectory,
                $"profile-photo-{Guid.NewGuid():N}{extension}");

            await using var source = await fileResult.OpenReadAsync();
            await using var destination = File.Create(destinationPath);
            await source.CopyToAsync(destination);

            return destinationPath;
        }

        private async Task CloseModalAsync()
        {
            if (Navigation.ModalStack.Contains(this))
            {
                await Navigation.PopModalAsync();
            }
        }
    }
}
