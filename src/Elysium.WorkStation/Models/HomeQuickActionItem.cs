namespace Elysium.WorkStation.Models;

public sealed class HomeQuickActionItem
{
    public HomeQuickActionItem(
        string title,
        string subtitle,
        string route,
        string iconGlyph,
        Color iconColor,
        Color iconBackgroundColor)
    {
        Title = title;
        Subtitle = subtitle;
        Route = route;
        IconGlyph = iconGlyph;
        IconColor = iconColor;
        IconBackgroundColor = iconBackgroundColor;
    }

    public string Title { get; }
    public string Subtitle { get; }
    public string Route { get; }
    public string IconGlyph { get; }
    public Color IconColor { get; }
    public Color IconBackgroundColor { get; }
}
