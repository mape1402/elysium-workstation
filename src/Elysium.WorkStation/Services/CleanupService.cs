namespace Elysium.WorkStation.Services
{
    public class CleanupService : ICleanupService
    {
        private readonly IFileRepository _fileRepository;
        private readonly IFileTransferService _fileTransferService;
        private readonly INotificationRepository _notificationRepository;
        private readonly IClipboardRepository _clipboardRepository;
        private readonly IClipboardSyncService _clipboardSyncService;
        private readonly ISettingsService _settingsService;
        private readonly string _serverFilesDir;

        private CancellationTokenSource _cts;
        private Task _backgroundTask;

        public CleanupService(
            IFileRepository fileRepository,
            IFileTransferService fileTransferService,
            INotificationRepository notificationRepository,
            IClipboardRepository clipboardRepository,
            IClipboardSyncService clipboardSyncService,
            ISettingsService settingsService)
        {
            _fileRepository         = fileRepository;
            _fileTransferService    = fileTransferService;
            _notificationRepository = notificationRepository;
            _clipboardRepository    = clipboardRepository;
            _clipboardSyncService   = clipboardSyncService;
            _settingsService        = settingsService;
            _serverFilesDir         = Path.Combine(FileSystem.AppDataDirectory, "files");
        }

        public Task StartAsync()
        {
            if (_cts is not null) return Task.CompletedTask;

            _cts = new CancellationTokenSource();
            _backgroundTask = RunAsync(_cts.Token);
            return Task.CompletedTask;
        }

        public async Task StopAsync()
        {
            if (_cts is null) return;

            await _cts.CancelAsync();
            try { await _backgroundTask; }
            catch (OperationCanceledException) { }
            finally
            {
                _cts.Dispose();
                _cts = null;
            }
        }

        private async Task RunAsync(CancellationToken ct)
        {
            await CleanupAsync();

            using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
            while (await timer.WaitForNextTickAsync(ct))
                await CleanupAsync();
        }

        private async Task CleanupAsync()
        {
            await CleanupFilesAsync(DateTime.Now.AddHours(-_settingsService.FileRetentionHours));
            await CleanupNotificationsAsync(DateTime.Now.AddHours(-_settingsService.NotificationRetentionHours));
            await CleanupClipboardAsync(DateTime.Now.AddHours(-_settingsService.ClipboardRetentionHours));
        }

        private async Task CleanupFilesAsync(DateTime cutoff)
        {
            try
            {
                var deleted = await _fileRepository.DeleteOlderThanAsync(cutoff);
                if (deleted.Count == 0) return;

                foreach (var entry in deleted)
                {
                    var fileDir = Path.Combine(_serverFilesDir, entry.FileId);
                    if (Directory.Exists(fileDir))
                    {
                        try { Directory.Delete(fileDir, recursive: true); }
                        catch { /* best-effort */ }
                    }
                }

                var deletedIds = deleted.Select(e => e.FileId).ToHashSet();
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    var history = _fileTransferService.History;
                    for (int i = history.Count - 1; i >= 0; i--)
                    {
                        if (deletedIds.Contains(history[i].FileId))
                            history.RemoveAt(i);
                    }
                });
            }
            catch { /* non-critical */ }
        }

        private async Task CleanupNotificationsAsync(DateTime cutoff)
        {
            try
            {
                await _notificationRepository.DeleteOlderThanAsync(cutoff);
            }
            catch { /* non-critical */ }
        }

        private async Task CleanupClipboardAsync(DateTime cutoff)
        {
            try
            {
                var deleted = await _clipboardRepository.DeleteOlderThanAsync(cutoff);
                if (deleted.Count == 0) return;

                var deletedIds = deleted.Select(e => e.Id).ToHashSet();
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    var history = _clipboardSyncService.History;
                    for (int i = history.Count - 1; i >= 0; i--)
                    {
                        if (deletedIds.Contains(history[i].Id))
                            history.RemoveAt(i);
                    }
                });
            }
            catch { /* non-critical */ }
        }
    }
}
