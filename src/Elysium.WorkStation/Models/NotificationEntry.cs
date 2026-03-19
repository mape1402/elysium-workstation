using System.ComponentModel.DataAnnotations.Schema;

namespace Elysium.WorkStation.Models
{
    public class NotificationEntry
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public bool IsRead { get; set; }

        [NotMapped]
        public string TimeDisplay => Timestamp.ToString("dd/MM · HH:mm");
    }
}
