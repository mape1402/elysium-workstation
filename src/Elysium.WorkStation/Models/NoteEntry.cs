using System.ComponentModel.DataAnnotations.Schema;

namespace Elysium.WorkStation.Models
{
    public class NoteEntry
    {
        private static readonly string[] PostItColors =
        [
            "#FFF9C4", // yellow
            "#FCE4EC", // pink
            "#E8F5E9", // green
            "#E3F2FD", // blue
            "#FFF3E0", // orange
            "#F3E5F5", // purple
        ];

        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Text { get; set; } = string.Empty;
        public string ColorHex { get; set; } = PostItColors[0];
        public DateTime Timestamp { get; set; } = DateTime.Now;

        [NotMapped]
        public string TimeDisplay => Timestamp.ToString("dd/MM · HH:mm");

        [NotMapped]
        public Color NoteColor => Color.FromArgb(ColorHex);

        public static string RandomColor() =>
            PostItColors[Random.Shared.Next(PostItColors.Length)];

        public static string[] AvailableColors => PostItColors;
    }
}
