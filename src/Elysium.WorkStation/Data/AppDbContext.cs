using Elysium.WorkStation.Models;
using Microsoft.EntityFrameworkCore;

namespace Elysium.WorkStation.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        public DbSet<NotificationEntry> Notifications => Set<NotificationEntry>();
    }
}
