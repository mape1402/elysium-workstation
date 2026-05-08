namespace Elysium.WorkStation.Services
{
    public interface IRemoteShellElevationService
    {
        Task<bool> EnsureHelperStartedAsync(bool interactivePrompt, CancellationToken cancellationToken = default);
        Task<bool> IsHelperAvailableAsync(CancellationToken cancellationToken = default);
        Task StopHelperAsync(CancellationToken cancellationToken = default);
        Task<bool> InterruptHelperSessionAsync(string sessionKey, CancellationToken cancellationToken = default);
        Task<int?> ExecuteInHelperSessionAsync(
            string sessionKey,
            string workingDirectory,
            string commandText,
            Func<string, bool, Task> onLineAsync,
            CancellationToken cancellationToken = default);
    }
}
