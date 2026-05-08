namespace Elysium.WorkStation.Services
{
    public sealed class NullRemoteShellElevationService : IRemoteShellElevationService
    {
        public Task<bool> EnsureHelperStartedAsync(bool interactivePrompt, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public Task<bool> IsHelperAvailableAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public Task StopHelperAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<bool> InterruptHelperSessionAsync(string sessionKey, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public Task<int?> ExecuteInHelperSessionAsync(
            string sessionKey,
            string workingDirectory,
            string commandText,
            Func<string, bool, Task> onLineAsync,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<int?>(null);
    }
}
