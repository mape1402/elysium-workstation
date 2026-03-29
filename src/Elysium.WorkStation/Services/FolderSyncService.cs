using System.Collections.ObjectModel;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Elysium.WorkStation.Models;
using Microsoft.AspNetCore.SignalR.Client;

namespace Elysium.WorkStation.Services
{
    public class FolderSyncService : IFolderSyncService
    {
        private const string ClientIdPreferenceKey = "folder_sync_client_id";
        private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

        private readonly IFolderSyncRepository _repository;
        private readonly ISettingsService _settingsService;
        private readonly INotificationService _notificationService;
        private readonly INotificationRepository _notificationRepository;

        private readonly object _runtimeGate = new();
        private readonly Dictionary<string, List<FolderSyncLogEntry>> _logsBySync = [];
        private readonly Dictionary<string, Dictionary<string, FolderSyncSummaryEntry>> _summaryBySync = [];
        private readonly Dictionary<string, FileSystemWatcher> _watchersBySync = [];
        private readonly Dictionary<string, SemaphoreSlim> _sendLocksBySync = [];
        private readonly Dictionary<string, DateTime> _eventDebounce = [];

        private HubConnection _connection;
        private CancellationTokenSource _startRetryCts;
        private string _baseUrl = string.Empty;
        private readonly string _clientName;
        private string ClientId => GetOrCreateClientId();

        public ObservableCollection<FolderSyncLink> Links { get; } = [];
        public ObservableCollection<FolderSyncInvite> PendingInvites { get; } = [];

        public bool IsConnected => _connection?.State == HubConnectionState.Connected;

        public event EventHandler StateChanged;

        public FolderSyncService(
            IFolderSyncRepository repository,
            ISettingsService settingsService,
            INotificationService notificationService,
            INotificationRepository notificationRepository)
        {
            _repository = repository;
            _settingsService = settingsService;
            _notificationService = notificationService;
            _notificationRepository = notificationRepository;
            _clientName = BuildClientName();
        }

        public async Task StartAsync(string hubUrl)
        {
            if (_connection is not null)
            {
                return;
            }

            _baseUrl = hubUrl[..hubUrl.LastIndexOf("/hubs/", StringComparison.Ordinal)];
            await ReloadAsync();

            var retryDelay = _settingsService.SignalRReconnectDelay;
            _connection = new HubConnectionBuilder()
                .WithUrl(hubUrl)
                .WithAutomaticReconnect(new MinuteRetryPolicy(retryDelay))
                .Build();

            RegisterHubHandlers(_connection);

            _connection.Reconnecting += _ =>
            {
                RaiseStateChanged();
                return Task.CompletedTask;
            };
            _connection.Reconnected += _ =>
            {
                RaiseStateChanged();
                return Task.CompletedTask;
            };
            _connection.Closed += _ =>
            {
                RaiseStateChanged();
                return Task.CompletedTask;
            };

            _startRetryCts = new CancellationTokenSource();
            var token = _startRetryCts.Token;
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await _connection.StartAsync(token);
                    RaiseStateChanged();
                    break;
                }
                catch
                {
                    try
                    {
                        await Task.Delay(TimeSpan.FromMinutes(1), token);
                    }
                    catch (TaskCanceledException)
                    {
                        break;
                    }
                }
            }
        }

        public async Task StopAsync()
        {
            lock (_runtimeGate)
            {
                foreach (var watcher in _watchersBySync.Values)
                {
                    watcher.EnableRaisingEvents = false;
                    watcher.Dispose();
                }
                _watchersBySync.Clear();
            }

            if (_connection is null)
            {
                return;
            }

            _startRetryCts?.Cancel();
            _startRetryCts?.Dispose();
            _startRetryCts = null;

            await _connection.StopAsync();
            await _connection.DisposeAsync();
            _connection = null;
            RaiseStateChanged();
        }

        public async Task ReloadAsync()
        {
            var links = await _repository.GetAllAsync();
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                Links.Clear();
                foreach (var link in links)
                {
                    Links.Add(link);
                }
            });
            RaiseStateChanged();
        }

        public async Task<FolderSyncLink> CreateSyncRequestAsync(
            string name,
            string description,
            string localFolderPath,
            IEnumerable<string> ignorePaths)
        {
            var normalizedName = name?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(normalizedName))
            {
                throw new InvalidOperationException("Debes indicar un nombre para la sincronizacion.");
            }

            var normalizedPath = NormalizeFolderPath(localFolderPath);
            if (!Directory.Exists(normalizedPath))
            {
                throw new DirectoryNotFoundException("La carpeta seleccionada no existe.");
            }

            var normalizedIgnores = NormalizeIgnorePaths(ignorePaths);
            var link = new FolderSyncLink
            {
                SyncId = Guid.NewGuid().ToString("N"),
                Name = normalizedName,
                Description = description?.Trim() ?? string.Empty,
                LocalFolderPath = normalizedPath,
                IgnorePathsJson = JsonSerializer.Serialize(normalizedIgnores),
                LocalClientId = ClientId,
                RemoteClientId = string.Empty,
                RemoteClientName = string.Empty,
                IsPendingOutgoing = false,
                IsPendingIncoming = false,
                IsAccepted = false,
                ContinuousSyncEnabled = false,
                IsEmitter = false,
                LastSnapshotJson = string.Empty,
                LastStateHash = string.Empty
            };

            link = await _repository.SaveAsync(link);
            await MainThread.InvokeOnMainThreadAsync(() => Links.Add(link));

            AddLog(link.SyncId, "config-created", string.Empty, $"Configuracion creada para '{link.Name}'.", isOutgoing: true);

            RaiseStateChanged();
            return link;
        }

        public async Task SendPairRequestAsync(int linkId)
        {
            var link = await _repository.GetByIdAsync(linkId);
            if (link is null)
            {
                throw new InvalidOperationException("Sincronizacion no encontrada.");
            }

            if (link.IsAccepted)
            {
                throw new InvalidOperationException("Esta sincronizacion ya esta vinculada.");
            }

            if (_connection?.State != HubConnectionState.Connected)
            {
                throw new InvalidOperationException("No hay conexion SignalR para enviar la solicitud.");
            }

            var inviteId = Guid.NewGuid().ToString("N");
            link.IsPendingOutgoing = true;
            link.IsPendingIncoming = false;
            link.RemoteClientId = string.Empty;
            link.RemoteClientName = string.Empty;
            link = await _repository.SaveAsync(link);
            await UpsertLinkInMemoryAsync(link);

            await SendFolderSyncInviteAsync(inviteId, link);

            AddLog(link.SyncId, "invite-out", string.Empty, $"Solicitud enviada para '{link.Name}'.", isOutgoing: true);
            RaiseStateChanged();
        }

        public async Task<FolderSyncLink> AcceptInviteAsync(FolderSyncInvite invite, string localFolderPath)
        {
            if (invite is null)
            {
                throw new InvalidOperationException("Invitacion invalida.");
            }

            var normalizedPath = NormalizeFolderPath(localFolderPath);
            if (!Directory.Exists(normalizedPath))
            {
                throw new DirectoryNotFoundException("La carpeta seleccionada no existe.");
            }

            var existing = await _repository.GetBySyncIdAsync(invite.SyncId);
            var link = existing ?? new FolderSyncLink
            {
                SyncId = invite.SyncId,
                CreatedAt = DateTime.Now
            };

            link.Name = invite.Name;
            link.Description = invite.Description;
            link.LocalFolderPath = normalizedPath;
            link.IgnorePathsJson = string.IsNullOrWhiteSpace(invite.IgnorePathsJson) ? "[]" : invite.IgnorePathsJson;
            link.LocalClientId = ClientId;
            link.RemoteClientId = invite.RequesterClientId;
            link.RemoteClientName = invite.RequesterName;
            link.IsPendingIncoming = false;
            link.IsPendingOutgoing = false;
            link.IsAccepted = true;
            link.ContinuousSyncEnabled = false;
            link.IsEmitter = false;

            link = await _repository.SaveAsync(link);
            await UpsertLinkInMemoryAsync(link);
            await RemoveInviteAsync(invite.InviteId);

            AddLog(link.SyncId, "invite-accept", string.Empty, $"Invitacion aceptada. Vinculada con {invite.RequesterName}.", isOutgoing: false);

            if (_connection?.State == HubConnectionState.Connected)
            {
                await SendFolderSyncInviteResponseAsync(invite.InviteId, invite.SyncId, accepted: true);
            }

            RaiseStateChanged();
            return link;
        }

        public async Task RejectInviteAsync(FolderSyncInvite invite)
        {
            if (invite is null)
            {
                return;
            }

            await RemoveInviteAsync(invite.InviteId);

            if (_connection?.State == HubConnectionState.Connected)
            {
                await SendFolderSyncInviteResponseAsync(invite.InviteId, invite.SyncId, accepted: false);
            }

            RaiseStateChanged();
        }

        public async Task SetContinuousAsync(int linkId, bool enabled)
        {
            var link = await _repository.GetByIdAsync(linkId);
            if (link is null)
            {
                throw new InvalidOperationException("Sincronizacion no encontrada.");
            }

            if (!link.IsAccepted)
            {
                throw new InvalidOperationException("La sincronizacion aun no ha sido aceptada.");
            }

            if (enabled)
            {
                ResetRuntime(link.SyncId);
                link.ContinuousSyncEnabled = true;
                link.IsEmitter = true;
                link = await _repository.SaveAsync(link);
                await UpsertLinkInMemoryAsync(link);

                await StartEmitterAsync(link, "Sincronizacion continua iniciada.");
                await BroadcastFolderSyncStateAsync(link.SyncId, enabled: true, emitterClientId: ClientId);
            }
            else
            {
                if (link.IsEmitter)
                {
                    await StopEmitterAsync(link, persistSnapshot: true, "Sincronizacion continua detenida.");
                }

                link.ContinuousSyncEnabled = false;
                link.IsEmitter = false;
                link = await _repository.SaveAsync(link);
                await UpsertLinkInMemoryAsync(link);

                await BroadcastFolderSyncStateAsync(link.SyncId, enabled: false, emitterClientId: string.Empty);
            }

            RaiseStateChanged();
        }

        public async Task SwitchRoleAsync(int linkId)
        {
            var link = await _repository.GetByIdAsync(linkId);
            if (link is null)
            {
                throw new InvalidOperationException("Sincronizacion no encontrada.");
            }

            if (!link.ContinuousSyncEnabled)
            {
                throw new InvalidOperationException("Debes habilitar sincronizacion continua para invertir el rol.");
            }

            if (string.IsNullOrWhiteSpace(link.RemoteClientId))
            {
                throw new InvalidOperationException("No hay cliente remoto asociado.");
            }

            if (link.IsEmitter)
            {
                await StopEmitterAsync(link, persistSnapshot: true, "Rol cambiado a receptor.");
                link.IsEmitter = false;
                link = await _repository.SaveAsync(link);
                await UpsertLinkInMemoryAsync(link);
                await BroadcastFolderSyncStateAsync(link.SyncId, enabled: true, emitterClientId: link.RemoteClientId);
            }
            else
            {
                link.IsEmitter = true;
                link = await _repository.SaveAsync(link);
                await UpsertLinkInMemoryAsync(link);
                await StartEmitterAsync(link, "Rol cambiado a emisor.");
                await BroadcastFolderSyncStateAsync(link.SyncId, enabled: true, emitterClientId: ClientId);
            }

            RaiseStateChanged();
        }

        public async Task UpdateIgnorePathsAsync(int linkId, IEnumerable<string> ignorePaths)
        {
            var link = await _repository.GetByIdAsync(linkId);
            if (link is null)
            {
                throw new InvalidOperationException("Sincronizacion no encontrada.");
            }

            var normalized = NormalizeIgnorePaths(ignorePaths);
            link.IgnorePathsJson = JsonSerializer.Serialize(normalized);
            link = await _repository.SaveAsync(link);
            await UpsertLinkInMemoryAsync(link);

            if (link.ContinuousSyncEnabled && link.IsEmitter)
            {
                await StartEmitterAsync(link, "Reglas de ignore actualizadas.");
            }

            RaiseStateChanged();
        }

        public IReadOnlyList<FolderSyncLogEntry> GetLogs(string syncId)
        {
            lock (_runtimeGate)
            {
                if (!_logsBySync.TryGetValue(syncId, out var entries))
                {
                    return [];
                }

                return entries
                    .OrderByDescending(e => e.Timestamp)
                    .ToList();
            }
        }

        public IReadOnlyList<FolderSyncSummaryEntry> GetSummary(string syncId)
        {
            lock (_runtimeGate)
            {
                if (!_summaryBySync.TryGetValue(syncId, out var map))
                {
                    return [];
                }

                return map.Values
                    .OrderByDescending(s => s.LastUpdatedAt)
                    .ThenBy(s => s.RelativePath)
                    .ToList();
            }
        }

        private void RegisterHubHandlers(HubConnection connection)
        {
            connection.On<string, string, string, string, string, string, string, string>(
                "ReceiveFolderSyncInvite",
                (inviteId, syncId, requesterClientId, requesterName, name, description, ignorePathsJson, requesterFolderPath) =>
                    _ = HandleInviteAsync(
                        inviteId,
                        syncId,
                        requesterClientId,
                        requesterName,
                        name,
                        description,
                        ignorePathsJson,
                        requesterFolderPath));

            connection.On<JsonElement>(
                "ReceiveFolderSyncInvitePayload",
                payload =>
                    _ = HandleInviteAsync(
                        ReadString(payload, "InviteId"),
                        ReadString(payload, "SyncId"),
                        ReadString(payload, "RequesterClientId"),
                        ReadString(payload, "RequesterName"),
                        ReadString(payload, "Name"),
                        ReadString(payload, "Description"),
                        ReadString(payload, "IgnorePathsJson", "[]"),
                        ReadString(payload, "RequesterFolderPath")));

            connection.On<string, string, bool, string, string>(
                "ReceiveFolderSyncInviteResponse",
                (inviteId, syncId, accepted, responderClientId, responderName) =>
                    _ = HandleInviteResponseAsync(
                        inviteId,
                        syncId,
                        accepted,
                        responderClientId,
                        responderName));

            connection.On<JsonElement>(
                "ReceiveFolderSyncInviteResponsePayload",
                payload =>
                    _ = HandleInviteResponseAsync(
                        ReadString(payload, "InviteId"),
                        ReadString(payload, "SyncId"),
                        ReadBool(payload, "Accepted"),
                        ReadString(payload, "ResponderClientId"),
                        ReadString(payload, "ResponderName")));

            connection.On<string, string, string, string, string, long, string>(
                "ReceiveFolderSyncChange",
                (syncId, senderClientId, action, relativePath, uploadId, fileSize, fileHash) =>
                    _ = HandleIncomingChangeAsync(
                        syncId,
                        senderClientId,
                        action,
                        relativePath,
                        uploadId,
                        fileSize,
                        fileHash));

            connection.On<string, bool, string, string>(
                "ReceiveFolderSyncState",
                (syncId, enabled, emitterClientId, changedByClientId) =>
                    _ = HandleFolderSyncStateAsync(syncId, enabled, emitterClientId, changedByClientId));
        }

        private async Task HandleInviteAsync(
            string inviteId,
            string syncId,
            string requesterClientId,
            string requesterName,
            string name,
            string description,
            string ignorePathsJson,
            string requesterFolderPath)
        {
            if (string.Equals(requesterClientId, ClientId, StringComparison.Ordinal))
            {
                return;
            }

            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                if (PendingInvites.Any(i => i.InviteId == inviteId))
                {
                    return;
                }

                PendingInvites.Add(new FolderSyncInvite
                {
                    InviteId = inviteId,
                    SyncId = syncId,
                    RequesterClientId = requesterClientId,
                    RequesterName = requesterName,
                    Name = name,
                    Description = description ?? string.Empty,
                    IgnorePathsJson = string.IsNullOrWhiteSpace(ignorePathsJson) ? "[]" : ignorePathsJson,
                    RequesterFolderPath = requesterFolderPath ?? string.Empty
                });
            });

            const string title = "Sincronizacion de carpeta";
            var message = $"{requesterName} solicita sincronizar '{name}'.";
            var payload = new FolderSyncInviteNotificationPayload
            {
                InviteId = inviteId,
                SyncId = syncId,
                RequesterClientId = requesterClientId,
                RequesterName = requesterName,
                Name = name,
                Description = description ?? string.Empty,
                IgnorePathsJson = string.IsNullOrWhiteSpace(ignorePathsJson) ? "[]" : ignorePathsJson,
                RequesterFolderPath = requesterFolderPath ?? string.Empty
            };

            try
            {
                await _notificationRepository.SaveAsync(new NotificationEntry
                {
                    Title = title,
                    Message = NotificationEntry.BuildFolderSyncInviteMessage(payload),
                    Timestamp = DateTime.Now
                });
            }
            catch
            {
                // Avoid breaking SignalR callback flow if local persistence fails.
            }

            MainThread.BeginInvokeOnMainThread(() => _notificationService.Notify(title, message));
            RaiseStateChanged();
        }

        private async Task HandleInviteResponseAsync(
            string inviteId,
            string syncId,
            bool accepted,
            string responderClientId,
            string responderName)
        {
            if (string.Equals(responderClientId, ClientId, StringComparison.Ordinal))
            {
                return;
            }

            var link = await _repository.GetBySyncIdAsync(syncId);
            if (link is null || !link.IsPendingOutgoing)
            {
                return;
            }

            if (accepted)
            {
                link.IsPendingOutgoing = false;
                link.IsAccepted = true;
                link.RemoteClientId = responderClientId;
                link.RemoteClientName = responderName;
                link = await _repository.SaveAsync(link);
                AddLog(link.SyncId, "invite-accepted", string.Empty, $"{responderName} acepto la solicitud.", isOutgoing: false);
            }
            else
            {
                AddLog(link.SyncId, "invite-rejected", string.Empty, $"{responderName} rechazo la solicitud.", isOutgoing: false);
            }

            await UpsertLinkInMemoryAsync(link);
            RaiseStateChanged();
        }

        private async Task HandleFolderSyncStateAsync(
            string syncId,
            bool enabled,
            string emitterClientId,
            string changedByClientId)
        {
            if (string.Equals(changedByClientId, ClientId, StringComparison.Ordinal))
            {
                return;
            }

            var link = await _repository.GetBySyncIdAsync(syncId);
            if (link is null)
            {
                return;
            }

            if (!enabled)
            {
                if (link.IsEmitter)
                {
                    await StopEmitterAsync(link, persistSnapshot: false, "Sincronizacion continua detenida por remoto.");
                }

                link.ContinuousSyncEnabled = false;
                link.IsEmitter = false;
                link = await _repository.SaveAsync(link);
                await UpsertLinkInMemoryAsync(link);
                RaiseStateChanged();
                return;
            }

            var amIEmitter = string.Equals(emitterClientId, ClientId, StringComparison.Ordinal);
            link.ContinuousSyncEnabled = true;
            link.IsEmitter = amIEmitter;
            link = await _repository.SaveAsync(link);
            await UpsertLinkInMemoryAsync(link);

            if (amIEmitter)
            {
                await StartEmitterAsync(link, "Este cliente ahora es emisor.");
            }
            else
            {
                StopWatcher(syncId);
                AddLog(syncId, "role-receiver", string.Empty, "Este cliente ahora es receptor.", isOutgoing: false);
            }

            RaiseStateChanged();
        }

        private async Task HandleIncomingChangeAsync(
            string syncId,
            string senderClientId,
            string action,
            string relativePath,
            string uploadId,
            long fileSize,
            string fileHash)
        {
            if (string.Equals(senderClientId, ClientId, StringComparison.Ordinal))
            {
                return;
            }

            var link = await _repository.GetBySyncIdAsync(syncId);
            if (link is null || !link.IsAccepted)
            {
                return;
            }

            if (link.IsEmitter)
            {
                // Cuando somos emisores ignoramos eventos remotos para evitar carreras.
                return;
            }

            var normalizedRelative = NormalizeRelativePath(relativePath);
            if (string.IsNullOrWhiteSpace(normalizedRelative))
            {
                return;
            }

            if (string.Equals(action, "delete", StringComparison.OrdinalIgnoreCase))
            {
                DeleteLocalFile(link.LocalFolderPath, normalizedRelative);
                AddLog(syncId, "delete-recv", normalizedRelative, $"Recibido delete: {normalizedRelative}", isOutgoing: false);
                AddSummary(syncId, normalizedRelative, action: "delete", isOutgoing: false);
                RaiseStateChanged();
                return;
            }

            if (string.IsNullOrWhiteSpace(uploadId))
            {
                return;
            }

            var destinationPath = BuildSafeDestinationPath(link.LocalFolderPath, normalizedRelative);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);

            using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(15) };
            using var response = await client.GetAsync(
                $"{_baseUrl}/api/folder-sync/download/{syncId}/{uploadId}",
                HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            await using var source = await response.Content.ReadAsStreamAsync();
            await using var destination = File.Create(destinationPath);
            await source.CopyToAsync(destination);
            await destination.FlushAsync();

            AddLog(syncId, "upsert-recv", normalizedRelative, $"Recibido update: {normalizedRelative} ({FormatSize(fileSize)}).", isOutgoing: false);
            AddSummary(syncId, normalizedRelative, action: "upsert", isOutgoing: false);
            RaiseStateChanged();
        }

        private async Task StartEmitterAsync(FolderSyncLink link, string reason)
        {
            if (!Directory.Exists(link.LocalFolderPath))
            {
                throw new DirectoryNotFoundException("La carpeta local ya no existe.");
            }

            StopWatcher(link.SyncId);
            AddLog(link.SyncId, "emitter-start", string.Empty, reason, isOutgoing: true);

            await SendDeltaSinceLastSnapshotAsync(link);
            StartWatcher(link);
            RaiseStateChanged();
        }

        private async Task StopEmitterAsync(FolderSyncLink link, bool persistSnapshot, string reason)
        {
            StopWatcher(link.SyncId);
            AddLog(link.SyncId, "emitter-stop", string.Empty, reason, isOutgoing: true);

            if (!persistSnapshot || !Directory.Exists(link.LocalFolderPath))
            {
                return;
            }

            var snapshot = await BuildSnapshotAsync(link.LocalFolderPath, GetIgnorePaths(link));
            link.LastSnapshotJson = JsonSerializer.Serialize(snapshot);
            link.LastStateHash = ComputeStateHash(snapshot);
            await _repository.SaveAsync(link);
            await UpsertLinkInMemoryAsync(link);
        }

        private async Task SendDeltaSinceLastSnapshotAsync(FolderSyncLink link)
        {
            var ignorePaths = GetIgnorePaths(link);
            var currentSnapshot = await BuildSnapshotAsync(link.LocalFolderPath, ignorePaths);
            var previousSnapshot = DeserializeSnapshot(link.LastSnapshotJson);

            var syncLock = GetSyncLock(link.SyncId);
            await syncLock.WaitAsync();
            try
            {
                foreach (var (relativePath, hash) in currentSnapshot)
                {
                    if (!previousSnapshot.TryGetValue(relativePath, out var previousHash) ||
                        !string.Equals(previousHash, hash, StringComparison.Ordinal))
                    {
                        var fullPath = Path.Combine(link.LocalFolderPath, relativePath.Replace('/', Path.DirectorySeparatorChar));
                        await SendUpsertAsync(link, relativePath, fullPath, hash);
                    }
                }

                foreach (var previousPath in previousSnapshot.Keys)
                {
                    if (!currentSnapshot.ContainsKey(previousPath))
                    {
                        await SendDeleteAsync(link, previousPath);
                    }
                }
            }
            finally
            {
                syncLock.Release();
            }
        }

        private void StartWatcher(FolderSyncLink link)
        {
            var watcher = new FileSystemWatcher(link.LocalFolderPath)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.Size | NotifyFilters.LastWrite
            };

            watcher.Created += (_, e) => _ = HandleWatcherChangedAsync(link.SyncId, WatcherChangeTypes.Created, e.FullPath);
            watcher.Changed += (_, e) => _ = HandleWatcherChangedAsync(link.SyncId, WatcherChangeTypes.Changed, e.FullPath);
            watcher.Deleted += (_, e) => _ = HandleWatcherChangedAsync(link.SyncId, WatcherChangeTypes.Deleted, e.FullPath);
            watcher.Renamed += (_, e) => _ = HandleWatcherRenamedAsync(link.SyncId, e.OldFullPath, e.FullPath);
            watcher.EnableRaisingEvents = true;

            lock (_runtimeGate)
            {
                _watchersBySync[link.SyncId] = watcher;
            }
        }

        private void StopWatcher(string syncId)
        {
            lock (_runtimeGate)
            {
                if (!_watchersBySync.TryGetValue(syncId, out var watcher))
                {
                    return;
                }

                watcher.EnableRaisingEvents = false;
                watcher.Dispose();
                _watchersBySync.Remove(syncId);
            }
        }

        private async Task HandleWatcherChangedAsync(string syncId, WatcherChangeTypes changeType, string fullPath)
        {
            if (Directory.Exists(fullPath))
            {
                return;
            }

            var link = await _repository.GetBySyncIdAsync(syncId);
            if (link is null || !link.ContinuousSyncEnabled || !link.IsEmitter || !Directory.Exists(link.LocalFolderPath))
            {
                return;
            }

            var relativePath = TryGetRelativePath(link.LocalFolderPath, fullPath);
            if (string.IsNullOrWhiteSpace(relativePath))
            {
                return;
            }

            var ignorePaths = GetIgnorePaths(link);
            if (IsIgnored(relativePath, ignorePaths))
            {
                return;
            }

            if (IsDebounced(syncId, changeType, relativePath))
            {
                return;
            }

            var syncLock = GetSyncLock(syncId);
            await syncLock.WaitAsync();
            try
            {
                if (changeType == WatcherChangeTypes.Deleted)
                {
                    await SendDeleteAsync(link, relativePath);
                    return;
                }

                if (!File.Exists(fullPath))
                {
                    return;
                }

                string hash;
                try
                {
                    hash = await ComputeFileHashAsync(fullPath);
                }
                catch
                {
                    return;
                }

                await SendUpsertAsync(link, relativePath, fullPath, hash);
            }
            finally
            {
                syncLock.Release();
            }
        }

        private async Task HandleWatcherRenamedAsync(string syncId, string oldPath, string newPath)
        {
            var link = await _repository.GetBySyncIdAsync(syncId);
            if (link is null || !link.ContinuousSyncEnabled || !link.IsEmitter)
            {
                return;
            }

            var oldRelative = TryGetRelativePath(link.LocalFolderPath, oldPath);
            if (!string.IsNullOrWhiteSpace(oldRelative))
            {
                await SendDeleteAsync(link, oldRelative);
            }

            await HandleWatcherChangedAsync(syncId, WatcherChangeTypes.Created, newPath);
        }

        private async Task SendUpsertAsync(FolderSyncLink link, string relativePath, string fullPath, string hash)
        {
            if (_connection?.State != HubConnectionState.Connected)
            {
                return;
            }

            var uploadId = await UploadFileForSyncAsync(link.SyncId, relativePath, fullPath);
            var fileSize = new FileInfo(fullPath).Length;

            await _connection.InvokeAsync(
                "AnnounceFolderSyncChange",
                link.SyncId,
                ClientId,
                "upsert",
                relativePath,
                uploadId,
                fileSize,
                hash);

            AddLog(link.SyncId, "upsert-send", relativePath, $"Enviado update: {relativePath} ({FormatSize(fileSize)}).", isOutgoing: true);
            AddSummary(link.SyncId, relativePath, action: "upsert", isOutgoing: true);
            RaiseStateChanged();
        }

        private async Task SendDeleteAsync(FolderSyncLink link, string relativePath)
        {
            if (_connection?.State != HubConnectionState.Connected)
            {
                return;
            }

            await _connection.InvokeAsync(
                "AnnounceFolderSyncChange",
                link.SyncId,
                ClientId,
                "delete",
                relativePath,
                string.Empty,
                0L,
                string.Empty);

            AddLog(link.SyncId, "delete-send", relativePath, $"Enviado delete: {relativePath}.", isOutgoing: true);
            AddSummary(link.SyncId, relativePath, action: "delete", isOutgoing: true);
            RaiseStateChanged();
        }

        private async Task<string> UploadFileForSyncAsync(string syncId, string relativePath, string fullPath)
        {
            using var content = new MultipartFormDataContent();
            content.Add(new StringContent(syncId), "syncId");
            content.Add(new StringContent(relativePath), "relativePath");
            await using var stream = File.OpenRead(fullPath);
            content.Add(new StreamContent(stream), "file", Path.GetFileName(fullPath));

            using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(15) };
            using var response = await client.PostAsync($"{_baseUrl}/api/folder-sync/upload", content);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<FolderSyncUploadResponse>(JsonOptions);
            if (result is null || string.IsNullOrWhiteSpace(result.UploadId))
            {
                throw new InvalidOperationException("No se obtuvo uploadId del servidor.");
            }

            return result.UploadId;
        }

        private static string BuildSafeDestinationPath(string rootFolderPath, string relativePath)
        {
            var normalized = relativePath
                .Replace('/', Path.DirectorySeparatorChar)
                .Replace('\\', Path.DirectorySeparatorChar);
            var fullRoot = Path.GetFullPath(rootFolderPath);
            var fullPath = Path.GetFullPath(Path.Combine(fullRoot, normalized));

            if (!fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Ruta de destino fuera de la carpeta sincronizada.");
            }

            return fullPath;
        }

        private static void DeleteLocalFile(string rootFolderPath, string relativePath)
        {
            var fullPath = BuildSafeDestinationPath(rootFolderPath, relativePath);
            if (File.Exists(fullPath))
            {
                File.Delete(fullPath);
            }
        }

        private async Task<Dictionary<string, string>> BuildSnapshotAsync(string rootFolderPath, IReadOnlyList<string> ignorePaths)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (!Directory.Exists(rootFolderPath))
            {
                return result;
            }

            var files = Directory.GetFiles(rootFolderPath, "*", SearchOption.AllDirectories);
            foreach (var file in files)
            {
                var relativePath = TryGetRelativePath(rootFolderPath, file);
                if (string.IsNullOrWhiteSpace(relativePath) || IsIgnored(relativePath, ignorePaths))
                {
                    continue;
                }

                try
                {
                    result[relativePath] = await ComputeFileHashAsync(file);
                }
                catch
                {
                    // Skip files that are currently locked.
                }
            }

            return result;
        }

        private static Dictionary<string, string> DeserializeSnapshot(string snapshotJson)
        {
            if (string.IsNullOrWhiteSpace(snapshotJson))
            {
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }

            try
            {
                return JsonSerializer.Deserialize<Dictionary<string, string>>(snapshotJson)
                    ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }
            catch
            {
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }
        }

        private static string ComputeStateHash(Dictionary<string, string> snapshot)
        {
            var canonical = string.Join(
                "\n",
                snapshot
                    .OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(kv => $"{kv.Key}|{kv.Value}"));

            using var sha = SHA256.Create();
            var bytes = System.Text.Encoding.UTF8.GetBytes(canonical);
            var hash = sha.ComputeHash(bytes);
            return Convert.ToHexString(hash);
        }

        private static async Task<string> ComputeFileHashAsync(string filePath)
        {
            using var sha = SHA256.Create();
            await using var stream = File.OpenRead(filePath);
            var hash = await sha.ComputeHashAsync(stream);
            return Convert.ToHexString(hash);
        }

        private static string TryGetRelativePath(string rootFolderPath, string fullPath)
        {
            if (string.IsNullOrWhiteSpace(rootFolderPath) || string.IsNullOrWhiteSpace(fullPath))
            {
                return string.Empty;
            }

            var rootFull = Path.GetFullPath(rootFolderPath);
            var full = Path.GetFullPath(fullPath);
            if (!full.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase))
            {
                return string.Empty;
            }

            var relative = Path.GetRelativePath(rootFull, full);
            if (relative.StartsWith("..", StringComparison.Ordinal))
            {
                return string.Empty;
            }

            return NormalizeRelativePath(relative);
        }

        private static string NormalizeRelativePath(string relativePath)
        {
            return (relativePath ?? string.Empty)
                .Trim()
                .Replace('\\', '/')
                .TrimStart('/');
        }

        private static IReadOnlyList<string> NormalizeIgnorePaths(IEnumerable<string> ignorePaths)
        {
            return (ignorePaths ?? [])
                .Select(path => NormalizeRelativePath(path))
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static bool IsIgnored(string relativePath, IReadOnlyList<string> ignorePaths)
        {
            foreach (var ignore in ignorePaths)
            {
                if (string.Equals(relativePath, ignore, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                if (relativePath.StartsWith(ignore + "/", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static string NormalizeFolderPath(string folderPath)
        {
            if (string.IsNullOrWhiteSpace(folderPath))
            {
                return string.Empty;
            }

            return Path.GetFullPath(folderPath.Trim());
        }

        private IReadOnlyList<string> GetIgnorePaths(FolderSyncLink link)
        {
            if (string.IsNullOrWhiteSpace(link.IgnorePathsJson))
            {
                return [];
            }

            try
            {
                var paths = JsonSerializer.Deserialize<List<string>>(link.IgnorePathsJson) ?? [];
                return NormalizeIgnorePaths(paths);
            }
            catch
            {
                return [];
            }
        }

        private SemaphoreSlim GetSyncLock(string syncId)
        {
            lock (_runtimeGate)
            {
                if (!_sendLocksBySync.TryGetValue(syncId, out var syncLock))
                {
                    syncLock = new SemaphoreSlim(1, 1);
                    _sendLocksBySync[syncId] = syncLock;
                }

                return syncLock;
            }
        }

        private bool IsDebounced(string syncId, WatcherChangeTypes changeType, string relativePath)
        {
            var key = $"{syncId}|{changeType}|{relativePath}";
            var now = DateTime.UtcNow;

            lock (_runtimeGate)
            {
                if (_eventDebounce.TryGetValue(key, out var previous) &&
                    now - previous < TimeSpan.FromMilliseconds(350))
                {
                    return true;
                }

                _eventDebounce[key] = now;
                return false;
            }
        }

        private async Task UpsertLinkInMemoryAsync(FolderSyncLink link)
        {
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                var index = Links.ToList().FindIndex(l => l.Id == link.Id);
                if (index < 0)
                {
                    Links.Add(link);
                    return;
                }

                Links[index] = link;
            });
        }

        private async Task RemoveInviteAsync(string inviteId)
        {
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                var existing = PendingInvites.FirstOrDefault(i => i.InviteId == inviteId);
                if (existing is not null)
                {
                    PendingInvites.Remove(existing);
                }
            });
        }

        private async Task BroadcastFolderSyncStateAsync(string syncId, bool enabled, string emitterClientId)
        {
            if (_connection?.State != HubConnectionState.Connected)
            {
                return;
            }

            await _connection.InvokeAsync(
                "AnnounceFolderSyncState",
                syncId,
                enabled,
                emitterClientId ?? string.Empty,
                ClientId);
        }

        private async Task SendFolderSyncInviteAsync(string inviteId, FolderSyncLink link)
        {
            if (_connection?.State != HubConnectionState.Connected)
            {
                throw new InvalidOperationException("No hay conexion SignalR para enviar la solicitud.");
            }

            try
            {
                await _connection.InvokeAsync(
                    "SendFolderSyncInvite",
                    inviteId,
                    link.SyncId,
                    ClientId,
                    _clientName,
                    link.Name,
                    link.Description,
                    link.IgnorePathsJson,
                    link.LocalFolderPath);
            }
            catch (Exception ex) when (IsMethodMissingHubError(ex))
            {
                await _connection.InvokeAsync(
                    "Broadcast",
                    "ReceiveFolderSyncInvitePayload",
                    new
                    {
                        InviteId = inviteId,
                        SyncId = link.SyncId,
                        RequesterClientId = ClientId,
                        RequesterName = _clientName,
                        Name = link.Name,
                        Description = link.Description,
                        IgnorePathsJson = link.IgnorePathsJson,
                        RequesterFolderPath = link.LocalFolderPath
                    });
            }
        }

        private async Task SendFolderSyncInviteResponseAsync(string inviteId, string syncId, bool accepted)
        {
            if (_connection?.State != HubConnectionState.Connected)
            {
                return;
            }

            try
            {
                await _connection.InvokeAsync(
                    "RespondFolderSyncInvite",
                    inviteId,
                    syncId,
                    accepted,
                    ClientId,
                    _clientName);
            }
            catch (Exception ex) when (IsMethodMissingHubError(ex))
            {
                await _connection.InvokeAsync(
                    "Broadcast",
                    "ReceiveFolderSyncInviteResponsePayload",
                    new
                    {
                        InviteId = inviteId,
                        SyncId = syncId,
                        Accepted = accepted,
                        ResponderClientId = ClientId,
                        ResponderName = _clientName
                    });
            }
        }

        private static bool IsMethodMissingHubError(Exception ex)
        {
            var message = ex.Message ?? string.Empty;
            return message.Contains("Method does not exist", StringComparison.OrdinalIgnoreCase)
                   || message.Contains("Failed to invoke", StringComparison.OrdinalIgnoreCase);
        }

        private static string ReadString(JsonElement payload, string propertyName, string fallback = "")
        {
            if (payload.ValueKind != JsonValueKind.Object)
            {
                return fallback;
            }

            if (!payload.TryGetProperty(propertyName, out var value))
            {
                return fallback;
            }

            return value.ValueKind == JsonValueKind.String
                ? value.GetString() ?? fallback
                : value.ToString() ?? fallback;
        }

        private static bool ReadBool(JsonElement payload, string propertyName, bool fallback = false)
        {
            if (payload.ValueKind != JsonValueKind.Object)
            {
                return fallback;
            }

            if (!payload.TryGetProperty(propertyName, out var value))
            {
                return fallback;
            }

            if (value.ValueKind == JsonValueKind.True)
            {
                return true;
            }

            if (value.ValueKind == JsonValueKind.False)
            {
                return false;
            }

            return bool.TryParse(value.ToString(), out var parsed) ? parsed : fallback;
        }

        private void AddLog(string syncId, string action, string relativePath, string message, bool isOutgoing)
        {
            lock (_runtimeGate)
            {
                if (!_logsBySync.TryGetValue(syncId, out var entries))
                {
                    entries = [];
                    _logsBySync[syncId] = entries;
                }

                entries.Add(new FolderSyncLogEntry
                {
                    Timestamp = DateTime.Now,
                    SyncId = syncId,
                    Action = action,
                    RelativePath = relativePath ?? string.Empty,
                    Message = message ?? string.Empty,
                    IsOutgoing = isOutgoing
                });

                if (entries.Count > 400)
                {
                    entries.RemoveRange(0, entries.Count - 400);
                }
            }
        }

        private void AddSummary(string syncId, string relativePath, string action, bool isOutgoing)
        {
            lock (_runtimeGate)
            {
                if (!_summaryBySync.TryGetValue(syncId, out var map))
                {
                    map = new Dictionary<string, FolderSyncSummaryEntry>(StringComparer.OrdinalIgnoreCase);
                    _summaryBySync[syncId] = map;
                }

                if (!map.TryGetValue(relativePath, out var summary))
                {
                    summary = new FolderSyncSummaryEntry
                    {
                        RelativePath = relativePath
                    };
                    map[relativePath] = summary;
                }

                var operation = "modify";
                if (string.Equals(action, "delete", StringComparison.OrdinalIgnoreCase))
                {
                    summary.DeletedCount++;
                    operation = "delete";
                }
                else if (isOutgoing)
                {
                    var hadActivity = summary.SentCount + summary.ReceivedCount + summary.DeletedCount > 0;
                    var comesAfterDelete = string.Equals(summary.LastAction, "delete", StringComparison.OrdinalIgnoreCase);
                    summary.SentCount++;
                    operation = !hadActivity || comesAfterDelete ? "add" : "modify";
                }
                else
                {
                    var hadActivity = summary.SentCount + summary.ReceivedCount + summary.DeletedCount > 0;
                    var comesAfterDelete = string.Equals(summary.LastAction, "delete", StringComparison.OrdinalIgnoreCase);
                    summary.ReceivedCount++;
                    operation = !hadActivity || comesAfterDelete ? "add" : "modify";
                }

                summary.LastAction = operation;
                summary.LastUpdatedAt = DateTime.Now;
            }
        }

        private void ResetRuntime(string syncId)
        {
            lock (_runtimeGate)
            {
                _logsBySync[syncId] = [];
                _summaryBySync[syncId] = new Dictionary<string, FolderSyncSummaryEntry>(StringComparer.OrdinalIgnoreCase);
            }
        }

        private static string GetOrCreateClientId()
        {
            var scopedKey = PreferenceScopeProvider.BuildScopedKey(ClientIdPreferenceKey);
            var storedClientId = Preferences.Default.Get(scopedKey, string.Empty);

#if DEBUG
            if (!string.IsNullOrWhiteSpace(storedClientId) &&
                PreferenceScopeProvider.CurrentGroup is PreferenceGroup.DebugClient or PreferenceGroup.DebugServer)
            {
                var otherScope = PreferenceScopeProvider.CurrentGroup == PreferenceGroup.DebugClient
                    ? PreferenceScopeProvider.DebugServerGroupName
                    : PreferenceScopeProvider.DebugClientGroupName;
                var otherScopedKey = $"{otherScope}.{ClientIdPreferenceKey}";
                var otherClientId = Preferences.Default.Get(otherScopedKey, string.Empty);
                if (!string.IsNullOrWhiteSpace(otherClientId) &&
                    string.Equals(otherClientId, storedClientId, StringComparison.Ordinal))
                {
                    storedClientId = string.Empty;
                }
            }
#endif

            if (!string.IsNullOrWhiteSpace(storedClientId))
            {
                return storedClientId;
            }

            var created = Guid.NewGuid().ToString("N");
            Preferences.Default.Set(scopedKey, created);
            return created;
        }

        private static string BuildClientName()
        {
            var machine = Environment.MachineName;
            return string.IsNullOrWhiteSpace(machine) ? "Cliente" : machine;
        }

        private static string FormatSize(long bytes)
        {
            if (bytes < 1_048_576)
            {
                return $"{bytes / 1024.0:F1} KB";
            }

            return $"{bytes / 1_048_576.0:F1} MB";
        }

        private void RaiseStateChanged()
        {
            MainThread.BeginInvokeOnMainThread(() => StateChanged?.Invoke(this, EventArgs.Empty));
        }

        private sealed record FolderSyncUploadResponse(string UploadId, long FileSize);
    }
}
