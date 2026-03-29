using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text.Json;
using Elysium.WorkStation.Models;
using Elysium.WorkStation.Services;

namespace Elysium.WorkStation.Views
{
    public sealed class FolderContentEntry
    {
        public string Name { get; set; } = string.Empty;
        public string FullPath { get; set; } = string.Empty;
        public string RelativePath { get; set; } = string.Empty;
        public bool IsDirectory { get; set; }
        public bool IsIgnored { get; set; }
        public string Icon => IsDirectory ? "\U0001F4C1" : "\U0001F4C4";
        public bool IsTracked
        {
            get => !IsIgnored;
            set => IsIgnored = !value;
        }
    }

    [QueryProperty(nameof(LinkIdQuery), "id")]
    public partial class FolderSyncDetailPage : ContentPage
    {
        private enum MonitorTab
        {
            Logs,
            Summary
        }

        private readonly IFolderSyncService _folderSyncService;
        private int _linkId;
        private FolderSyncLink _link;
        private bool _isReloading;
        private bool _suppressNextStateReload;
        private MonitorTab _selectedMonitorTab = MonitorTab.Logs;
        private bool _isMonitorSectionExpanded;
        private bool _isFolderMaximized;
        private string _folderRootPath = string.Empty;
        private string _currentFolderViewPath = string.Empty;

        public ObservableCollection<string> IgnorePaths { get; } = [];
        public ObservableCollection<FolderSyncLogEntry> Logs { get; } = [];
        public ObservableCollection<FolderSyncSummaryEntry> Summary { get; } = [];
        public ObservableCollection<FolderContentEntry> FolderEntries { get; } = [];

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
            ? "\U0001F7E2  Conectado al canal de sincronizacion"
            : "\U0001F534  Sin conexion";

        public Color StatusColor => _folderSyncService.IsConnected
            ? Color.FromArgb("#1B5E20")
            : Color.FromArgb("#B71C1C");

        public string LinkName => _link?.Name ?? "Sincronizacion";
        public string LinkDescription => _link?.Description ?? string.Empty;
        public string LinkPath => _link?.LocalFolderPath ?? string.Empty;
        public Color LinkOverallStatusFillColor
        {
            get
            {
                if (_link?.IsAccepted != true)
                {
                    return Color.FromArgb("#C5CEDD");
                }

                return _link.ContinuousSyncEnabled
                    ? Color.FromArgb("#43C36C")
                    : Color.FromArgb("#F2C94C");
            }
        }
        public Color LinkOverallStatusStrokeColor
        {
            get
            {
                if (_link?.IsAccepted != true)
                {
                    return Color.FromArgb("#9CAAC2");
                }

                return _link.ContinuousSyncEnabled
                    ? Color.FromArgb("#2F9E56")
                    : Color.FromArgb("#D4A52E");
            }
        }
        public string LinkStatusText
        {
            get
            {
                if (_link is null)
                {
                    return "Sin estado";
                }

                if (_link.IsAccepted)
                {
                    return _link.ContinuousSyncEnabled
                        ? "Conectada - Sincronizando"
                        : "Conectada - En espera";
                }

                if (_link.IsPendingIncoming)
                {
                    return "Solicitud recibida - Pendiente";
                }

                if (_link.IsPendingOutgoing)
                {
                    return "Solicitud enviada - Pendiente";
                }

                return "Sin conectar";
            }
        }
        public string LinkRoleText
        {
            get
            {
                if (_link?.ContinuousSyncEnabled != true)
                {
                    return "Ninguno";
                }

                return _link.IsEmitter ? "Emisor" : "Receptor";
            }
        }
        public bool IsContinuousEnabled => _link?.ContinuousSyncEnabled == true;
        public bool IsSyncStopped => _link?.ContinuousSyncEnabled != true;
        public bool CanShowSummaryTab => IsContinuousEnabled;
        public bool IsLogsTabSelected => _selectedMonitorTab == MonitorTab.Logs || !CanShowSummaryTab;
        public bool IsSummaryTabSelected => CanShowSummaryTab && _selectedMonitorTab == MonitorTab.Summary;
        public bool IsLogsContentVisible => !IsSummaryTabSelected;
        public bool IsSummaryContentVisible => IsSummaryTabSelected;
        public bool IsFolderSectionVisible => IsSyncStopped && !IsMonitorSectionVisible;
        public bool ArePrimarySectionsVisible => !_isFolderMaximized;
        public bool CanSwitchRole => _link?.ContinuousSyncEnabled == true;
        public bool CanToggleMonitorSection => IsSyncStopped;
        public bool IsMonitorSectionVisible => !_isFolderMaximized && (IsContinuousEnabled || _isMonitorSectionExpanded);
        public string FolderMaximizeButtonIcon => _isFolderMaximized ? "\u2750" : "\u26F6";
        public string ToggleMonitorSectionText => _isMonitorSectionExpanded
            ? "\U0001F9FE Ocultar bitacora"
            : "\U0001F9FE Mostrar bitacora";
        public string ToggleContinuousText => IsContinuousEnabled
            ? "\U0001F6D1 Detener"
            : "\u25B6 Iniciar";
        public bool CanSendPairRequest => _link is not null && !_link.IsAccepted;
        public string SendPairRequestText => _link?.IsPendingOutgoing == true
            ? "\U0001F501 Reenviar solicitud"
            : "\U0001F4E1 Enviar solicitud";
        public string SwitchRoleButtonText => "\U0001F504 Invertir rol";
        public string OpenFolderButtonText => "\U0001F4C2 Abrir carpeta";
        public string ManageIgnorePathsButtonText => "\U0001F4CB Rutas ignoradas";
        public string IgnorePathsStatus => IgnorePaths.Count == 0
            ? "Sin rutas ignoradas"
            : $"{IgnorePaths.Count} ruta(s) ignorada(s)";
        public bool CanGoBackFolderView =>
            !string.IsNullOrWhiteSpace(_folderRootPath) &&
            !string.IsNullOrWhiteSpace(_currentFolderViewPath) &&
            !string.Equals(
                NormalizeFolderPath(_folderRootPath),
                NormalizeFolderPath(_currentFolderViewPath),
                StringComparison.OrdinalIgnoreCase);

        public string CurrentFolderViewPath
        {
            get => _currentFolderViewPath;
            private set
            {
                _currentFolderViewPath = value ?? string.Empty;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanGoBackFolderView));
            }
        }

        public Command OpenFolderCommand { get; }
        public Command SendPairRequestCommand { get; }
        public Command ToggleContinuousCommand { get; }
        public Command SwitchRoleCommand { get; }
        public Command OpenIgnorePathsEditorCommand { get; }
        public Command<FolderContentEntry> OpenFolderEntryCommand { get; }
        public Command<FolderContentEntry> ToggleIgnorePathCommand { get; }
        public Command GoUpFolderCommand { get; }
        public Command SelectLogsTabCommand { get; }
        public Command SelectSummaryTabCommand { get; }
        public Command ToggleMonitorSectionCommand { get; }
        public Command ToggleFolderMaximizeCommand { get; }

        public FolderSyncDetailPage(IFolderSyncService folderSyncService)
        {
            _folderSyncService = folderSyncService;

            OpenFolderCommand = new Command(async () =>
            {
                if (_link is null || string.IsNullOrWhiteSpace(_link.LocalFolderPath))
                {
                    return;
                }

                try
                {
                    OpenFolder(_link.LocalFolderPath);
                }
                catch (Exception ex)
                {
                    await DisplayAlert("Sincronizacion", ex.Message, "OK");
                }
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
                if (_link is null)
                {
                    return;
                }

                try
                {
                    await _folderSyncService.SetContinuousAsync(_link.Id, !_link.ContinuousSyncEnabled);
                    await ReloadLinkAsync(reloadFromRepository: true);
                }
                catch (Exception ex)
                {
                    await DisplayAlert("Sincronizacion", ex.Message, "OK");
                }
            });

            SwitchRoleCommand = new Command(async () =>
            {
                if (_link is null)
                {
                    return;
                }

                try
                {
                    await _folderSyncService.SwitchRoleAsync(_link.Id);
                    await ReloadLinkAsync(reloadFromRepository: true);
                }
                catch (Exception ex)
                {
                    await DisplayAlert("Sincronizacion", ex.Message, "OK");
                }
            });

            OpenIgnorePathsEditorCommand = new Command(async () =>
            {
                if (_link is null)
                {
                    return;
                }

                try
                {
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
                }
                catch (Exception ex)
                {
                    await DisplayAlert("Sincronizacion", ex.Message, "OK");
                }
            });

            OpenFolderEntryCommand = new Command<FolderContentEntry>(entry =>
            {
                if (entry is null)
                {
                    return;
                }

                if (entry.IsDirectory)
                {
                    LoadFolderEntries(entry.FullPath);
                    return;
                }

                OpenFile(entry.FullPath);
            });

            ToggleIgnorePathCommand = new Command<FolderContentEntry>(async entry =>
            {
                if (entry is null || string.IsNullOrWhiteSpace(entry.RelativePath))
                {
                    return;
                }

                var existing = IgnorePaths.FirstOrDefault(path =>
                    string.Equals(NormalizeRelativePath(path), entry.RelativePath, StringComparison.OrdinalIgnoreCase));

                if (existing is null)
                {
                    IgnorePaths.Add(entry.RelativePath);
                }
                else
                {
                    IgnorePaths.Remove(existing);
                }

                await PersistIgnorePathsAsync();
            });

            GoUpFolderCommand = new Command(() =>
            {
                if (string.IsNullOrWhiteSpace(_currentFolderViewPath))
                {
                    return;
                }

                var parent = Directory.GetParent(_currentFolderViewPath);
                if (parent is null)
                {
                    return;
                }

                var next = Path.GetFullPath(parent.FullName);
                if (!string.IsNullOrWhiteSpace(_folderRootPath) &&
                    !next.StartsWith(_folderRootPath, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                LoadFolderEntries(next);
            });
            SelectLogsTabCommand = new Command(() => SetActiveMonitorTab(MonitorTab.Logs));
            SelectSummaryTabCommand = new Command(() =>
            {
                if (!CanShowSummaryTab)
                {
                    return;
                }

                SetActiveMonitorTab(MonitorTab.Summary);
            });
            ToggleMonitorSectionCommand = new Command(() =>
            {
                if (!IsSyncStopped)
                {
                    return;
                }

                _isMonitorSectionExpanded = !_isMonitorSectionExpanded;
                OnPropertyChanged(nameof(IsMonitorSectionVisible));
                OnPropertyChanged(nameof(IsFolderSectionVisible));
                OnPropertyChanged(nameof(ToggleMonitorSectionText));
            });
            ToggleFolderMaximizeCommand = new Command(() =>
            {
                if (!IsSyncStopped)
                {
                    return;
                }

                _isFolderMaximized = !_isFolderMaximized;
                if (_isFolderMaximized)
                {
                    _isMonitorSectionExpanded = false;
                }

                OnPropertyChanged(nameof(ArePrimarySectionsVisible));
                OnPropertyChanged(nameof(IsMonitorSectionVisible));
                OnPropertyChanged(nameof(IsFolderSectionVisible));
                OnPropertyChanged(nameof(FolderMaximizeButtonIcon));
                OnPropertyChanged(nameof(ToggleMonitorSectionText));
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

            var previousFolderViewPath = NormalizeFolderPath(CurrentFolderViewPath);

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

                _folderRootPath = NormalizeFolderPath(_link.LocalFolderPath);
                var folderToLoad = _folderRootPath;
                if (!string.IsNullOrWhiteSpace(previousFolderViewPath) &&
                    previousFolderViewPath.StartsWith(_folderRootPath, StringComparison.OrdinalIgnoreCase) &&
                    Directory.Exists(previousFolderViewPath))
                {
                    folderToLoad = previousFolderViewPath;
                }

                LoadFolderEntries(folderToLoad);

                Logs.Clear();
                foreach (var log in _folderSyncService.GetLogs(_link.SyncId))
                {
                    Logs.Add(log);
                }

                Summary.Clear();
                foreach (var item in _folderSyncService
                    .GetSummary(_link.SyncId)
                    .Where(s => !string.IsNullOrWhiteSpace(s.RelativePath)))
                {
                    Summary.Add(item);
                }

                OnPropertyChanged(nameof(StatusText));
                OnPropertyChanged(nameof(StatusColor));
                OnPropertyChanged(nameof(LinkName));
                OnPropertyChanged(nameof(LinkDescription));
                OnPropertyChanged(nameof(LinkPath));
                OnPropertyChanged(nameof(LinkOverallStatusFillColor));
                OnPropertyChanged(nameof(LinkOverallStatusStrokeColor));
                OnPropertyChanged(nameof(LinkStatusText));
                OnPropertyChanged(nameof(LinkRoleText));
                OnPropertyChanged(nameof(CanSendPairRequest));
                OnPropertyChanged(nameof(SendPairRequestText));
                OnPropertyChanged(nameof(ToggleContinuousText));
                OnPropertyChanged(nameof(IsContinuousEnabled));
                OnPropertyChanged(nameof(IsSyncStopped));
                OnPropertyChanged(nameof(CanSwitchRole));
                if (IsContinuousEnabled)
                {
                    _isMonitorSectionExpanded = true;
                    _isFolderMaximized = false;
                }
                else if (!IsSyncStopped)
                {
                    _isFolderMaximized = false;
                }
                OnPropertyChanged(nameof(ArePrimarySectionsVisible));
                OnPropertyChanged(nameof(CanToggleMonitorSection));
                OnPropertyChanged(nameof(IsMonitorSectionVisible));
                OnPropertyChanged(nameof(IsFolderSectionVisible));
                OnPropertyChanged(nameof(FolderMaximizeButtonIcon));
                OnPropertyChanged(nameof(ToggleMonitorSectionText));
                OnPropertyChanged(nameof(IgnorePathsStatus));
                OnPropertyChanged(nameof(CurrentFolderViewPath));
                EnsureValidActiveMonitorTab();
            }
            finally
            {
                _isReloading = false;
            }
        }

        private void SetActiveMonitorTab(MonitorTab tab)
        {
            var next = tab;
            if (next == MonitorTab.Summary && !CanShowSummaryTab)
            {
                next = MonitorTab.Logs;
            }

            if (_selectedMonitorTab != next)
            {
                _selectedMonitorTab = next;
            }

            RefreshMonitorTabBindings();
        }

        private void EnsureValidActiveMonitorTab()
        {
            if (!CanShowSummaryTab && _selectedMonitorTab == MonitorTab.Summary)
            {
                _selectedMonitorTab = MonitorTab.Logs;
            }

            RefreshMonitorTabBindings();
        }

        private void RefreshMonitorTabBindings()
        {
            OnPropertyChanged(nameof(CanShowSummaryTab));
            OnPropertyChanged(nameof(IsLogsTabSelected));
            OnPropertyChanged(nameof(IsSummaryTabSelected));
            OnPropertyChanged(nameof(IsLogsContentVisible));
            OnPropertyChanged(nameof(IsSummaryContentVisible));
            OnPropertyChanged(nameof(IsFolderSectionVisible));
        }

        private async Task PersistIgnorePathsAsync()
        {
            if (_link is null)
            {
                return;
            }

            OnPropertyChanged(nameof(IgnorePathsStatus));
            _link.IgnorePathsJson = JsonSerializer.Serialize(IgnorePaths.ToList());
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

                if (_suppressNextStateReload)
                {
                    _suppressNextStateReload = false;
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

        private void LoadFolderEntries(string path)
        {
            FolderEntries.Clear();

            var normalized = NormalizeFolderPath(path);
            CurrentFolderViewPath = normalized;

            if (!Directory.Exists(normalized))
            {
                return;
            }

            try
            {
                foreach (var dir in Directory.GetDirectories(normalized).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
                {
                    var relativePath = ToRelativePath(_folderRootPath, dir);
                    var ignored = IsIgnoredRelativePath(relativePath);
                    FolderEntries.Add(new FolderContentEntry
                    {
                        Name = Path.GetFileName(dir),
                        FullPath = dir,
                        RelativePath = relativePath,
                        IsDirectory = true,
                        IsIgnored = ignored
                    });
                }

                foreach (var file in Directory.GetFiles(normalized).OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
                {
                    var relativePath = ToRelativePath(_folderRootPath, file);
                    var ignored = IsIgnoredRelativePath(relativePath);
                    FolderEntries.Add(new FolderContentEntry
                    {
                        Name = Path.GetFileName(file),
                        FullPath = file,
                        RelativePath = relativePath,
                        IsDirectory = false,
                        IsIgnored = ignored
                    });
                }
            }
            catch
            {
                // Keep UI responsive if folder cannot be read.
            }
        }

        private bool IsIgnoredRelativePath(string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath))
            {
                return false;
            }

            return IgnorePaths.Any(path =>
                string.Equals(NormalizeRelativePath(path), relativePath, StringComparison.OrdinalIgnoreCase));
        }

        private static string ToRelativePath(string rootPath, string fullPath)
        {
            if (string.IsNullOrWhiteSpace(rootPath) || string.IsNullOrWhiteSpace(fullPath))
            {
                return string.Empty;
            }

            try
            {
                var rootFull = Path.GetFullPath(rootPath.Trim());
                var entryFull = Path.GetFullPath(fullPath.Trim());

                if (!entryFull.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase))
                {
                    return string.Empty;
                }

                var relative = Path.GetRelativePath(rootFull, entryFull);
                return NormalizeRelativePath(relative);
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string NormalizeRelativePath(string relativePath)
        {
            return (relativePath ?? string.Empty)
                .Trim()
                .Replace('\\', '/')
                .TrimStart('/');
        }

        private static string NormalizeFolderPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }

            try
            {
                return Path.GetFullPath(path);
            }
            catch
            {
                return path;
            }
        }

        private void OnFolderEntryPointerEntered(object sender, PointerEventArgs e)
        {
            if (sender is not Border border)
            {
                return;
            }

            ApplyFolderEntryHover(border, hover: true);
        }

        private void OnFolderEntryPointerExited(object sender, PointerEventArgs e)
        {
            if (sender is not Border border)
            {
                return;
            }

            ApplyFolderEntryHover(border, hover: false);
        }

        private static void ApplyFolderEntryHover(Border border, bool hover)
        {
            border.BackgroundColor = hover
                ? ResolveFolderEntryHoverBackground()
                : Colors.Transparent;
            border.Stroke = Colors.Transparent;
            border.StrokeThickness = 0;
            border.Scale = 1.0;
            border.TranslationX = 0;
            border.TranslationY = 0;
        }

        private static Color ResolveFolderEntryHoverBackground()
        {
            var requestedTheme = Application.Current?.RequestedTheme ?? AppTheme.Light;
            return requestedTheme == AppTheme.Dark
                ? Color.FromArgb("#2A3A55")
                : Color.FromArgb("#EDF4FF");
        }

        private async void OnTrackCheckBoxChanged(object sender, CheckedChangedEventArgs e)
        {
            if (_isReloading || _link is null)
            {
                return;
            }

            if (sender is not CheckBox checkBox || checkBox.BindingContext is not FolderContentEntry entry)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(entry.RelativePath))
            {
                return;
            }

            var existing = IgnorePaths.FirstOrDefault(path =>
                string.Equals(NormalizeRelativePath(path), entry.RelativePath, StringComparison.OrdinalIgnoreCase));

            var shouldTrack = e.Value;
            var changed = false;

            if (shouldTrack)
            {
                if (existing is not null)
                {
                    IgnorePaths.Remove(existing);
                    changed = true;
                }
            }
            else if (existing is null)
            {
                IgnorePaths.Add(entry.RelativePath);
                changed = true;
            }

            if (!changed)
            {
                return;
            }

            entry.IsIgnored = !shouldTrack;
            try
            {
                OnPropertyChanged(nameof(IgnorePathsStatus));
                _link.IgnorePathsJson = JsonSerializer.Serialize(IgnorePaths.ToList());
                _suppressNextStateReload = true;
                await _folderSyncService.UpdateIgnorePathsAsync(_link.Id, IgnorePaths.ToList());
            }
            catch (Exception ex)
            {
                _suppressNextStateReload = false;
                await DisplayAlert("Sincronizacion", ex.Message, "OK");
                await ReloadLinkAsync(reloadFromRepository: false);
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

        private static void OpenFile(string path)
        {
#if WINDOWS
            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            });
#endif
        }
    }
}

