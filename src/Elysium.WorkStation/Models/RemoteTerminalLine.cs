namespace Elysium.WorkStation.Models
{
    public sealed class RemoteTerminalLine
    {
        public string Text { get; set; } = string.Empty;
        public Color ForegroundColor { get; set; } = Color.FromArgb("#D4D4D4");
    }
}
