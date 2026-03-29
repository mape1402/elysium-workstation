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
        private bool _isReloading;

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
        public bool CanSendPairRequest => _link is not null && !_link.IsAccepted;
        public string SendPairRequestText => _link?.IsPendingOutgoing == true
            ? "Reenviar solicitud"
            : "Enviar solicitud";
        public string IgnorePathsStatus => IgnorePaths.Count == 0
            ? "Sin rutas ignoradas"
            : $"{IgnorePaths.Count} ruta(s) ignorada(s)";

        public Command OpenFolderCommand { get; }
        public Command SendPairRequestCommand { get; }
        public Command ToggleContinuousCommand { get; }
        public Command SwitchRoleCommand { get; }
        public Command OpenIgnorePathsEditorCommand { get; }

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

            SendPairRequestCommand = new Command(async () =>
            {
                if (_link is null)
                {
                    return;
                }

                try
                {
                    await _folderSyncService.SendPairRequestAsync(_link.Id);
                    await ReloadLinkAsync(reloadFromRepository: true);
                }
                catch (Exception ex)
                {
                    await DisplayAlert("Sincronizacion", ex.Message, "OK");
                }
            });

            ToggleContinuousCommand = new Command(async () =>
            {
                if (_link is null) return;
                await _folderSyncService.SetContinuousAsync(_link.Id, !_link.ContinuousSyncEnabled);
                await ReloadLinkAsync(reloadFromRepository: true);
            });

            SwitchRoleCommand = new Command(async () =>
            {
                if (_link is null) return;
                await _folderSyncService.SwitchRoleAsync(_link.Id);
                await ReloadLinkAsync(reloadFromRepository: true);
            });

            OpenIgnorePathsEditorCommand = new Command(async () =>
            {
                if (_link is null) return;

                var editor = new IgnorePathsEditorPage(_link.LocalFolderPath, IgnorePaths);
                await Navigation.PushModalAsync(editor);
                var result = await editor.ResultTask;
                if (result is null)
                {
                    return;
                }

                IgnorePaths.Clear();
                foreach (var item in result.Where(path => !string.IsNullOrWhiteSpace(path)).Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    IgnorePaths.Add(item);
                }

                await PersistIgnorePathsAsync();
            });

            InitializeComponent();
            BindingContext = this;
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();
            _folderSyncService.StateChanged -= OnServiceStateChanged;
            _folderSyncService.StateChanged += OnServiceStateChanged;
            await ReloadLinkAsync(reloadFromRepository: true);
        }

        protected override void OnDisappearing()
        {
            _folderSyncService.StateChanged -= OnServiceStateChanged;
            base.OnDisappearing();
        }

        private async Task ReloadLinkAsync(bool reloadFromRepository)
        {
            if (_isReloading)
            {
                return;
            }

            _isReloading = true;
            try
            {
                if (reloadFromRepository)
                {
                    await _folderSyncService.ReloadAsync();
                }

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
                OnPropertyChanged(nameof(CanSendPairRequest));
                OnPropertyChanged(nameof(SendPairRequestText));
                OnPropertyChanged(nameof(ToggleContinuousText));
                OnPropertyChanged(nameof(IgnorePathsStatus));
            }
            finally
            {
                _isReloading = false;
            }
        }

        private async Task PersistIgnorePathsAsync()
        {
            if (_link is null)
            {
                return;
            }

            OnPropertyChanged(nameof(IgnorePathsStatus));
            await _folderSyncService.UpdateIgnorePathsAsync(_link.Id, IgnorePaths.ToList());
            await ReloadLinkAsync(reloadFromRepository: true);
        }

        private void OnServiceStateChanged(object sender, EventArgs e)
        {
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                if (_link is null)
                {
                    return;
                }

                await ReloadLinkAsync(reloadFromRepository: false);
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
