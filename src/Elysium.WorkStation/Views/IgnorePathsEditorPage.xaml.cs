using System.Collections.ObjectModel;
using Elysium.WorkStation.Services;

namespace Elysium.WorkStation.Views
{
    public sealed class IgnorePathItem
    {
        public string Path { get; set; } = string.Empty;
        public string Icon { get; set; } = "\U0001F4C4";
        public bool IsPattern { get; set; }
    }

    public partial class IgnorePathsEditorPage : ContentPage
    {
        private const string PatternIcon = "\U0001F9E9";
        private const string FileIcon = "\U0001F4C4";
        private const string FolderIcon = "\U0001F4C1";

        private readonly string _rootPath;
        private readonly TaskCompletionSource<IReadOnlyList<string>?> _resultSource = new();

        public ObservableCollection<IgnorePathItem> IgnorePathItems { get; } = [];
        public string RootPath => _rootPath;

        public Command AddPathCommand { get; }
        public Command CancelCommand { get; }
        public Command SaveCommand { get; }
        public Command<IgnorePathItem> RemovePathCommand { get; }

        public Task<IReadOnlyList<string>?> ResultTask => _resultSource.Task;

        public IgnorePathsEditorPage(string rootPath, IEnumerable<string> currentPaths)
        {
            _rootPath = string.IsNullOrWhiteSpace(rootPath)
                ? string.Empty
                : Path.GetFullPath(rootPath.Trim());

            foreach (var path in currentPaths
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Select(IgnorePathMatcher.NormalizeEntry)
                .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                AddIgnorePathItem(path);
            }

            AddPathCommand = new Command(async () => await AddPathAsync());
            CancelCommand = new Command(async () => await CancelAsync());
            SaveCommand = new Command(async () => await SaveAsync());
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
            var popup = new IgnorePatternPromptPage(
                title: "Path ignore",
                message: "Ingresa un patron wildcard (ej: *.xml) o regex (ej: regex:^temp_\\d+\\.txt$).",
                placeholder: "*.xml");
            await Navigation.PushModalAsync(popup);
            var input = await popup.ResultTask;
            if (string.IsNullOrWhiteSpace(input))
            {
                return;
            }

            var normalized = IgnorePathMatcher.NormalizeEntry(input);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                await DisplayAlert("Path ignore", "El patron no es valido.", "OK");
                return;
            }

            if (!IgnorePathMatcher.IsValidRegexEntry(normalized))
            {
                await DisplayAlert("Path ignore", "El regex no es valido. Usa formato: regex:tu_patron", "OK");
                return;
            }

            if (IgnorePathItems.Any(item => string.Equals(item.Path, normalized, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            AddIgnorePathItem(normalized);
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

        private async Task CancelAsync()
        {
            _resultSource.TrySetResult(null);
            await CloseModalAsync();
        }

        private async Task SaveAsync()
        {
            var result = IgnorePathItems
                .Select(item => item.Path)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            _resultSource.TrySetResult(result);
            await CloseModalAsync();
        }

        private async Task CloseModalAsync()
        {
            if (Navigation.ModalStack.Contains(this))
            {
                await Navigation.PopModalAsync();
            }
        }

        private void AddIgnorePathItem(string path)
        {
            var normalized = IgnorePathMatcher.NormalizeEntry(path);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return;
            }

            if (IgnorePathItems.Any(item => string.Equals(item.Path, normalized, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            IgnorePathItems.Add(new IgnorePathItem
            {
                Path = normalized,
                IsPattern = IgnorePathMatcher.IsPattern(normalized),
                Icon = ResolveIcon(normalized)
            });
        }

        private string ResolveIcon(string normalized)
        {
            if (IgnorePathMatcher.IsPattern(normalized))
            {
                return PatternIcon;
            }

            if (string.IsNullOrWhiteSpace(_rootPath) || !Directory.Exists(_rootPath))
            {
                return Path.HasExtension(normalized) ? FileIcon : FolderIcon;
            }

            try
            {
                var absolute = Path.GetFullPath(Path.Combine(_rootPath, normalized.Replace('/', Path.DirectorySeparatorChar)));
                if (!absolute.StartsWith(_rootPath, StringComparison.OrdinalIgnoreCase))
                {
                    return Path.HasExtension(normalized) ? FileIcon : FolderIcon;
                }

                if (Directory.Exists(absolute))
                {
                    return FolderIcon;
                }

                if (File.Exists(absolute))
                {
                    return FileIcon;
                }
            }
            catch
            {
                // Keep fallback below.
            }

            return Path.HasExtension(normalized) ? FileIcon : FolderIcon;
        }
    }
}
