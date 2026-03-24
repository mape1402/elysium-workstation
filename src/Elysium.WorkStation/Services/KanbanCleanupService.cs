namespace Elysium.WorkStation.Services
{
    public class KanbanCleanupService : IKanbanCleanupService
    {
        private readonly IKanbanTaskRepository _repository;
        private readonly ISettingsService _settings;
        private CancellationTokenSource? _cts;
        private Task? _backgroundTask;

        public KanbanCleanupService(IKanbanTaskRepository repository, ISettingsService settings)
        {
            _repository = repository;
            _settings = settings;
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
            try { if (_backgroundTask is not null) await _backgroundTask; }
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

            using var timer = new PeriodicTimer(TimeSpan.FromHours(_settings.KanbanCleanupIntervalHours));
            while (await timer.WaitForNextTickAsync(ct))
                await CleanupAsync();
        }

        private async Task CleanupAsync()
        {
            try
            {
                var cutoff = DateTime.Now.AddDays(-_settings.KanbanCleanupRetentionDays);
                await _repository.HideCompletedOlderThanAsync(cutoff);
            }
            catch { /* non-critical */ }
        }
    }
}
