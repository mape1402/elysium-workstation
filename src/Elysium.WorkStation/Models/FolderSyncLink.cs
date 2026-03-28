using System.ComponentModel.DataAnnotations.Schema;

namespace Elysium.WorkStation.Models
{
    public class FolderSyncLink
    {
        public int Id { get; set; }
        public string SyncId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string LocalFolderPath { get; set; } = string.Empty;
        public string IgnorePathsJson { get; set; } = "[]";
        public string LocalClientId { get; set; } = string.Empty;
        public string RemoteClientId { get; set; } = string.Empty;
        public string RemoteClientName { get; set; } = string.Empty;
        public bool IsPendingOutgoing { get; set; }
        public bool IsPendingIncoming { get; set; }
        public bool IsAccepted { get; set; }
        public bool ContinuousSyncEnabled { get; set; }
        public bool IsEmitter { get; set; }
        public string LastSnapshotJson { get; set; } = string.Empty;
        public string LastStateHash { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime UpdatedAt { get; set; } = DateTime.Now;

        [NotMapped]
        public string StatusText => IsPendingIncoming
            ? "Invitacion pendiente"
            : IsPendingOutgoing
                ? "Solicitud enviada"
                : IsAccepted
                    ? "Conectada"
                    : "Sin estado";

        [NotMapped]
        public string RoleText => IsEmitter ? "Emisor" : "Receptor";

        [NotMapped]
        public string ContinuousButtonText => ContinuousSyncEnabled
            ? "Detener continua"
            : "Iniciar continua";
    }
}
