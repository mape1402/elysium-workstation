using System.ComponentModel.DataAnnotations.Schema;

namespace Elysium.WorkStation.Models
{
    public enum KanbanStatus
    {
        Pending,
        InProgress,
        Blocked,
        Done
    }

    public enum KanbanPriority
    {
        Low,
        Medium,
        High,
        Critical
    }

    public class KanbanTask
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public KanbanStatus Status { get; set; } = KanbanStatus.Pending;
        public KanbanPriority Priority { get; set; } = KanbanPriority.Medium;
        public bool Visible { get; set; } = true;
        public DateTime? CompletedOn { get; set; }
        public int SortOrder { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        [NotMapped]
        public string StatusDisplay => Status switch
        {
            KanbanStatus.Pending    => "⏳ Pendiente",
            KanbanStatus.InProgress => "🔄 En Progreso",
            KanbanStatus.Blocked    => "⚠️ Bloqueado",
            KanbanStatus.Done       => "✅ Terminado",
            _                       => Status.ToString()
        };

        [NotMapped]
        public Color StatusColor => Status switch
        {
            KanbanStatus.Pending    => Color.FromArgb("#9E9E9E"),
            KanbanStatus.InProgress => Color.FromArgb("#1E88E5"),
            KanbanStatus.Blocked    => Color.FromArgb("#FB8C00"),
            KanbanStatus.Done       => Color.FromArgb("#43A047"),
            _                       => Colors.Grey
        };

        [NotMapped]
        public string PriorityDisplay => Priority switch
        {
            KanbanPriority.Low      => "🔽 Baja",
            KanbanPriority.Medium   => "➖ Media",
            KanbanPriority.High     => "🔼 Alta",
            KanbanPriority.Critical => "🔴 Crítica",
            _                       => Priority.ToString()
        };

        [NotMapped]
        public Color PriorityColor => Priority switch
        {
            KanbanPriority.Low      => Color.FromArgb("#78909C"),
            KanbanPriority.Medium   => Color.FromArgb("#FFB300"),
            KanbanPriority.High     => Color.FromArgb("#FB8C00"),
            KanbanPriority.Critical => Color.FromArgb("#E53935"),
            _                       => Colors.Grey
        };

        [NotMapped]
        public string PriorityIcon => Priority switch
        {
            KanbanPriority.Low      => "🔽",
            KanbanPriority.Medium   => "➖",
            KanbanPriority.High     => "🔼",
            KanbanPriority.Critical => "🔴",
            _                       => "➖"
        };

        [NotMapped]
        public string TimeDisplay => CreatedAt.ToString("dd/MM · HH:mm");
    }
}
