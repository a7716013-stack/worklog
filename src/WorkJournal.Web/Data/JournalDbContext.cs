using Microsoft.EntityFrameworkCore;
using WorkJournal.Web.Models;
namespace WorkJournal.Web.Data;

public class JournalDbContext(DbContextOptions<JournalDbContext> options) : DbContext(options)
{
    public DbSet<WorkLog> WorkLogs => Set<WorkLog>();
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<WorkLog>().Property(x => x.Hours).HasPrecision(5, 2);
        modelBuilder.Entity<WorkLog>().HasIndex(x => x.WorkDate);
        modelBuilder.Entity<WorkLog>().HasIndex(x => x.Status);
    }
}

