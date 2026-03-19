namespace Elysium.WorkStation.Services
{
    public class FileCleanupService : IFileCleanupService
    {
        private readonly IFileRepository _fileRepository;
        private readonly IFileTransferService _fileTransferService;
        private readonly ISettingsService _settingsService;
        private readonly string _serverFilesDir;

        private CancellationTokenSource _cts;
        private Task _backgroundTask;

        public FileCleanupService(
            IFileRepository fileRepository,
            IFileTransferService fileTransferService,
            ISettingsService settingsService)
        {
            _fileRepository      = fileRepository;
            _fileTransferService = fileTransferService;
            _settingsService     = settingsService;
            _serverFilesDir      = Path.Combine(FileSystem.AppDataDirectory, "files");
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
            // Initial cleanup on start.
            await CleanupAsync();

            using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
            while (await timer.WaitForNextTickAsync(ct))
                await CleanupAsync();
        }

        private async Task CleanupAsync()
        {
            try
            {
                var cutoff = DateTime.Now.AddHours(-_settingsService.FileRetentionHours);
                var deleted = await _fileRepository.DeleteOlderThanAsync(cutoff);

                if (deleted.Count == 0) return;

                // Remove physical files stored by the server.
                foreach (var entry in deleted)
                {
                    var fileDir = Path.Combine(_serverFilesDir, entry.FileId);
                    if (Directory.Exists(fileDir))
                    {
                        try { Directory.Delete(fileDir, recursive: true); }
                        catch { /* best-effort */ }
                    }
                }

                // Keep the in-memory History collection in sync.
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
            catch
            {
                // Non-critical background task; swallow exceptions to keep the timer alive.
            }
        }
    }
}
