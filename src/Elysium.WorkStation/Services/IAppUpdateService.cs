namespace Elysium.WorkStation.Services
{
    public interface IAppUpdateService
    {
        string CurrentVersion { get; }

        Task<AppUpdateInfo> CheckLatestAsync(CancellationToken cancellationToken = default);

        Task DownloadAndApplyAsync(
            AppUpdateInfo update,
            IProgress<AppUpdateProgress> progress = null,
            CancellationToken cancellationToken = default);
    }

    public sealed class AppUpdateInfo
    {
        public string CurrentVersion { get; init; } = string.Empty;
        public string LatestTag { get; init; } = string.Empty;
        public string LatestVersion { get; init; } = string.Empty;
        public string ReleaseUrl { get; init; } = string.Empty;
        public string AssetName { get; init; } = string.Empty;
        public string AssetDownloadUrl { get; init; } = string.Empty;
        public long AssetSizeBytes { get; init; }
        public bool IsUpdateAvailable { get; init; }
        public string Message { get; init; } = string.Empty;
    }

    public sealed class AppUpdateProgress
    {
        public string Message { get; init; } = string.Empty;
        public double Progress { get; init; }
        public long BytesReceived { get; init; }
        public long TotalBytes { get; init; }
    }
}
