using System.ComponentModel.DataAnnotations.Schema;

namespace Elysium.WorkStation.Models
{
    public class WorkVariable
    {
        public int Id { get; set; }
        public int GroupId { get; set; }
        public string VariableKey { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public bool IsSecret { get; set; }
        public string EncryptedValue { get; set; } = string.Empty;
        public DateTime UpdatedAt { get; set; } = DateTime.Now;

        [NotMapped]
        public string UpdatedAtText => UpdatedAt.ToString("dd/MM · HH:mm");
    }
}
