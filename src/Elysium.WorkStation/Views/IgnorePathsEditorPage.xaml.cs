using System.Collections.ObjectModel;

namespace Elysium.WorkStation.Views
{
    public sealed class IgnorePathItem
    {
        public string Path { get; set; } = string.Empty;
    }

    public partial class IgnorePathsEditorPage : ContentPage
    {
        private readonly string _rootPath;
        private readonly TaskCompletionSource<IReadOnlyList<string>?> _resultSource = new();

        public ObservableCollection<IgnorePathItem> IgnorePathItems { get; } = [];
        public string RootPath => _rootPath;

        public Command AddPathCommand { get; }
        public Command SaveCommand { get; }
        public Command CancelCommand { get; }
        public Command<IgnorePathItem> RemovePathCommand { get; }

        public Task<IReadOnlyList<string>?> ResultTask => _resultSource.Task;

        public IgnorePathsEditorPage(string rootPath, IEnumerable<string> currentPaths)
        {
            _rootPath = string.IsNullOrWhiteSpace(rootPath)
                ? string.Empty
                : Path.GetFullPath(rootPath.Trim());

            foreach (var path in currentPaths
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Select(item => item.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                IgnorePathItems.Add(new IgnorePathItem { Path = path });
            }

            AddPathCommand = new Command(async () => await AddPathAsync());
            SaveCommand = new Command(async () => await SaveAsync());
            CancelCommand = new Command(async () => await CancelAsync());
            RemovePathCommand = new Command<IgnorePathItem>(RemovePath);

            InitializeComponent();
            BindingContext = this;
        }

        protected override void OnDisappearing()
        {
            if (!_resultSource.Task.IsCompleted)
            {
                _resultSource.TrySetResult(null);
            }

            base.OnDisappearing();
        }

        private async Task AddPathAsync()
        {
            if (string.IsNullOrWhiteSpace(_rootPath) || !Directory.Exists(_rootPath))
            {
                await DisplayAlert("Path ignore", "La carpeta sincronizada no es valida.", "OK");
                return;
            }

            var picker = new IgnorePathPickerPage(_rootPath);
            await Navigation.PushModalAsync(picker);
            var selectedPath = await picker.ResultTask;
            if (string.IsNullOrWhiteSpace(selectedPath))
            {
                return;
            }

            var relative = ToRelativeIgnorePath(_rootPath, selectedPath);
            if (string.IsNullOrWhiteSpace(relative))
            {
                await DisplayAlert("Path ignore", "La ruta debe estar dentro de la carpeta sincronizada.", "OK");
                return;
            }

            if (IgnorePathItems.Any(item => string.Equals(item.Path, relative, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            IgnorePathItems.Add(new IgnorePathItem { Path = relative });
        }

        private void RemovePath(IgnorePathItem item)
        {
            if (item is null)
            {
                return;
            }

            var existing = IgnorePathItems.FirstOrDefault(x =>
                string.Equals(x.Path, item.Path, StringComparison.OrdinalIgnoreCase));
            if (existing is null)
            {
                return;
            }

            IgnorePathItems.Remove(existing);
        }

        private async Task SaveAsync()
        {
            _resultSource.TrySetResult(IgnorePathItems.Select(item => item.Path).ToList());
            await CloseModalAsync();
        }

        private async Task CancelAsync()
        {
            _resultSource.TrySetResult(null);
            await CloseModalAsync();
        }

        private async Task CloseModalAsync()
        {
            if (Navigation.ModalStack.Contains(this))
            {
                await Navigation.PopModalAsync();
            }
        }

        private static string ToRelativeIgnorePath(string rootFolderPath, string selectedPath)
        {
            if (string.IsNullOrWhiteSpace(rootFolderPath) || string.IsNullOrWhiteSpace(selectedPath))
            {
                return string.Empty;
            }

            var rootFull = Path.GetFullPath(rootFolderPath.Trim());
            var selectedFull = Path.GetFullPath(selectedPath.Trim());
            if (!selectedFull.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase))
            {
                return string.Empty;
            }

            var relative = Path.GetRelativePath(rootFull, selectedFull)
                .Replace('\\', '/')
                .Trim()
                .TrimStart('/');

            if (string.IsNullOrWhiteSpace(relative) || relative.StartsWith("..", StringComparison.Ordinal))
            {
                return string.Empty;
            }

            return relative;
        }
    }
}
