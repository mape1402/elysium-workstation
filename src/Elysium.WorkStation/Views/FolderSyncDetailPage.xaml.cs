using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text.Json;
using Elysium.WorkStation.Models;
using Elysium.WorkStation.Services;

namespace Elysium.WorkStation.Views
{
    [QueryProperty(nameof(LinkIdQuery), "id")]
    public partial class FolderSyncDetailPage : ContentPage
    {
        private readonly IFolderSyncService _folderSyncService;
        private int _linkId;
        private FolderSyncLink _link;

        public ObservableCollection<string> IgnorePaths { get; } = [];
        public ObservableCollection<FolderSyncLogEntry> Logs { get; } = [];
        public ObservableCollection<FolderSyncSummaryEntry> Summary { get; } = [];

        public string LinkIdQuery
        {
            set
            {
                if (int.TryParse(value, out var parsed))
                {
                    _linkId = parsed;
                }
            }
        }

        public string StatusText => _folderSyncService.IsConnected
            ? "🟢  Conectado al canal de sincronizacion"
            : "🔴  Sin conexion";

        public Color StatusColor => _folderSyncService.IsConnected
            ? Color.FromArgb("#1B5E20")
            : Color.FromArgb("#B71C1C");

        public string LinkName => _link?.Name ?? "Sincronizacion";
        public string LinkDescription => _link?.Description ?? string.Empty;
        public string LinkPath => _link?.LocalFolderPath ?? string.Empty;
        public string LinkStatusText => _link?.StatusText ?? string.Empty;
        public string LinkRoleText => _link?.RoleText ?? string.Empty;
        public string ToggleContinuousText => _link?.ContinuousButtonText ?? "Iniciar continua";

        public Command OpenFolderCommand { get; }
        public Command ToggleContinuousCommand { get; }
        public Command SwitchRoleCommand { get; }
        public Command AddIgnorePathCommand { get; }
        public Command<string> RemoveIgnorePathCommand { get; }

        public FolderSyncDetailPage(IFolderSyncService folderSyncService)
        {
            _folderSyncService = folderSyncService;

            OpenFolderCommand = new Command(() =>
            {
                if (_link is null || string.IsNullOrWhiteSpace(_link.LocalFolderPath))
                {
                    return;
                }

                OpenFolder(_link.LocalFolderPath);
            });

            ToggleContinuousCommand = new Command(async () =>
            {
                if (_link is null) return;
                await _folderSyncService.SetContinuousAsync(_link.Id, !_link.ContinuousSyncEnabled);
                await ReloadLinkAsync();
            });

            SwitchRoleCommand = new Command(async () =>
            {
                if (_link is null) return;
                await _folderSyncService.SwitchRoleAsync(_link.Id);
                await ReloadLinkAsync();
            });

            AddIgnorePathCommand = new Command(async () =>
            {
                if (_link is null) return;

                var picker = new IgnorePathPickerPage(_link.LocalFolderPath);
                await Navigation.PushModalAsync(picker);
                var selectedPath = await picker.ResultTask;
                if (string.IsNullOrWhiteSpace(selectedPath))
                {
                    return;
                }

                var relative = ToRelativeIgnorePath(_link.LocalFolderPath, selectedPath);
                if (string.IsNullOrWhiteSpace(relative))
                {
                    await DisplayAlert("Sincronizacion", "La ruta seleccionada debe estar dentro de la carpeta sincronizada.", "OK");
                    return;
                }

                if (IgnorePaths.Any(path => string.Equals(path, relative, StringComparison.OrdinalIgnoreCase)))
                {
                    return;
                }

                IgnorePaths.Add(relative);
                await PersistIgnorePathsAsync();
            });

            RemoveIgnorePathCommand = new Command<string>(async path =>
            {
                if (string.IsNullOrWhiteSpace(path))
                {
                    return;
                }

                var existing = IgnorePaths.FirstOrDefault(item => string.Equals(item, path, StringComparison.OrdinalIgnoreCase));
                if (existing is null)
                {
                    return;
                }

                IgnorePaths.Remove(existing);
                await PersistIgnorePathsAsync();
            });

            InitializeComponent();
            BindingContext = this;

            _folderSyncService.StateChanged += OnServiceStateChanged;
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();
            await ReloadLinkAsync();
        }

        protected override void OnDisappearing()
        {
            base.OnDisappearing();
        }

        private async Task ReloadLinkAsync()
        {
            await _folderSyncService.ReloadAsync();
            _link = _folderSyncService.Links.FirstOrDefault(item => item.Id == _linkId);

            if (_link is null)
            {
                await DisplayAlert("Sincronizacion", "No se encontro la carpeta de sincronizacion.", "OK");
                await Shell.Current.GoToAsync("..");
                return;
            }

            IgnorePaths.Clear();
            foreach (var path in ParseIgnorePathsJson(_link.IgnorePathsJson))
            {
                IgnorePaths.Add(path);
            }

            Logs.Clear();
            foreach (var log in _folderSyncService.GetLogs(_link.SyncId))
            {
                Logs.Add(log);
            }

            Summary.Clear();
            foreach (var item in _folderSyncService.GetSummary(_link.SyncId))
            {
                Summary.Add(item);
            }

            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(StatusColor));
            OnPropertyChanged(nameof(LinkName));
            OnPropertyChanged(nameof(LinkDescription));
            OnPropertyChanged(nameof(LinkPath));
            OnPropertyChanged(nameof(LinkStatusText));
            OnPropertyChanged(nameof(LinkRoleText));
            OnPropertyChanged(nameof(ToggleContinuousText));
        }

        private async Task PersistIgnorePathsAsync()
        {
            if (_link is null)
            {
                return;
            }

            await _folderSyncService.UpdateIgnorePathsAsync(_link.Id, IgnorePaths.ToList());
            await ReloadLinkAsync();
        }

        private void OnServiceStateChanged(object sender, EventArgs e)
        {
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                if (_link is null)
                {
                    return;
                }

                await ReloadLinkAsync();
            });
        }

        private static List<string> ParseIgnorePathsJson(string ignorePathsJson)
        {
            if (string.IsNullOrWhiteSpace(ignorePathsJson))
            {
                return [];
            }

            try
            {
                return JsonSerializer.Deserialize<List<string>>(ignorePathsJson) ?? [];
            }
            catch
            {
                return [];
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

        private static void OpenFolder(string path)
        {
#if WINDOWS
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{path}\"",
                UseShellExecute = true
            });
#endif
        }
    }
}
