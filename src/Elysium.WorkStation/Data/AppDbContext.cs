using Elysium.WorkStation.Models;
using Microsoft.EntityFrameworkCore;

namespace Elysium.WorkStation.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        public DbSet<NotificationEntry> Notifications  => Set<NotificationEntry>();
        public DbSet<ClipboardEntry>    ClipboardHistory => Set<ClipboardEntry>();
        public DbSet<FileEntry>         FileHistory      => Set<FileEntry>();
        public DbSet<NoteEntry>         Notes            => Set<NoteEntry>();
        public DbSet<KanbanTask>        KanbanTasks      => Set<KanbanTask>();
        public DbSet<BrainstormNode>    BrainstormNodes  => Set<BrainstormNode>();
        public DbSet<VariableGroup>     VariableGroups   => Set<VariableGroup>();
        public DbSet<WorkVariable>      WorkVariables    => Set<WorkVariable>();
        public DbSet<FolderSyncLink>    FolderSyncLinks  => Set<FolderSyncLink>();
    }
}
