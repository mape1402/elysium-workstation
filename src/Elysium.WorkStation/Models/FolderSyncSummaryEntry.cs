using Microsoft.Maui.Graphics;

namespace Elysium.WorkStation.Models
{
    public class FolderSyncSummaryEntry
    {
        public string RelativePath { get; set; } = string.Empty;
        public int SentCount { get; set; }
        public int ReceivedCount { get; set; }
        public int DeletedCount { get; set; }
        public DateTime LastUpdatedAt { get; set; } = DateTime.Now;
        public string LastAction { get; set; } = string.Empty;

        public string FileName
        {
            get
            {
                var normalized = (RelativePath ?? string.Empty).Replace('/', Path.DirectorySeparatorChar);
                var name = Path.GetFileName(normalized);
                return string.IsNullOrWhiteSpace(name) ? (RelativePath ?? string.Empty) : name;
            }
        }

        public string OperationLabel => GetOperationCode() switch
        {
            "add" => "Agregado",
            "delete" => "Eliminado",
            _ => "Modificado"
        };

        public Color OperationColor => GetOperationCode() switch
        {
            "add" => Color.FromArgb("#2E9D4A"),
            "delete" => Color.FromArgb("#CF3F3F"),
            _ => Color.FromArgb("#E1B22A")
        };

        public bool IsDeleted => string.Equals(GetOperationCode(), "delete", StringComparison.OrdinalIgnoreCase);

        private string GetOperationCode()
        {
            var action = (LastAction ?? string.Empty).Trim().ToLowerInvariant();
            if (action is "add" or "modify" or "delete")
            {
                return action;
            }

            if (action == "upsert")
            {
                return (SentCount + ReceivedCount <= 1 && DeletedCount == 0) ? "add" : "modify";
            }

            if (action == "remove")
            {
                return "delete";
            }

            return "modify";
        }
    }
}
