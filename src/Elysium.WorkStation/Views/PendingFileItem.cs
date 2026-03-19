namespace Elysium.WorkStation.Views
{
    public sealed class PendingFileItem(string fullPath)
    {
        public string FullPath { get; } = fullPath;
        public string FileName => Path.GetFileName(FullPath);
    }
}
