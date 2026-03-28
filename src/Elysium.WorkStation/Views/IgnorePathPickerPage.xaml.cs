using System.Collections.ObjectModel;

namespace Elysium.WorkStation.Views
{
    public sealed class IgnorePathPickerEntry
    {
        public string Name { get; set; } = string.Empty;
        public string FullPath { get; set; } = string.Empty;
        public bool IsDirectory { get; set; }
        public string Icon => IsDirectory ? "📁" : "📄";
        public string ActionText => IsDirectory ? "Entrar" : "Seleccionar";
    }

    public partial class IgnorePathPickerPage : ContentPage
    {
        private readonly string _rootPath;
        private readonly TaskCompletionSource<string?> _resultSource = new();
        private string _currentPath;

        public ObservableCollection<IgnorePathPickerEntry> Entries { get; } = [];
        public string CurrentPath
        {
            get => _currentPath;
            private set
            {
                _currentPath = value;
                OnPropertyChanged();
            }
        }

        public Task<string?> ResultTask => _resultSource.Task;

        public IgnorePathPickerPage(string rootPath)
        {
            _rootPath = Path.GetFullPath(rootPath);
            _currentPath = _rootPath;

            InitializeComponent();
            BindingContext = this;

            LoadEntries(_currentPath);
        }

        protected override void OnDisappearing()
        {
            if (!_resultSource.Task.IsCompleted)
            {
                _resultSource.TrySetResult(null);
            }

            base.OnDisappearing();
        }

        private void LoadEntries(string path)
        {
            if (!Directory.Exists(path))
            {
                return;
            }

            CurrentPath = path;
            Entries.Clear();

            foreach (var dir in Directory.GetDirectories(path).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
            {
                Entries.Add(new IgnorePathPickerEntry
                {
                    Name = Path.GetFileName(dir),
                    FullPath = dir,
                    IsDirectory = true
                });
            }

            foreach (var file in Directory.GetFiles(path).OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            {
                Entries.Add(new IgnorePathPickerEntry
                {
                    Name = Path.GetFileName(file),
                    FullPath = file,
                    IsDirectory = false
                });
            }
        }

        private void OnEntryActionClicked(object sender, EventArgs e)
        {
            if (sender is not Button button || button.CommandParameter is not IgnorePathPickerEntry entry)
            {
                return;
            }

            if (entry.IsDirectory)
            {
                LoadEntries(entry.FullPath);
                return;
            }

            _ = FinishAsync(entry.FullPath);
        }

        private void OnGoUpClicked(object sender, EventArgs e)
        {
            var parent = Directory.GetParent(_currentPath);
            if (parent is null)
            {
                return;
            }

            var parentPath = Path.GetFullPath(parent.FullName);
            if (!parentPath.StartsWith(_rootPath, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            LoadEntries(parentPath);
        }

        private void OnPickCurrentFolderClicked(object sender, EventArgs e)
        {
            _ = FinishAsync(_currentPath);
        }

        private async void OnCancelClicked(object sender, EventArgs e)
        {
            _resultSource.TrySetResult(null);
            await CloseModalAsync();
        }

        private async Task FinishAsync(string path)
        {
            _resultSource.TrySetResult(path);
            await CloseModalAsync();
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
