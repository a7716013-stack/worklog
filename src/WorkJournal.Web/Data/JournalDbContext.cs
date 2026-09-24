using Microsoft.EntityFrameworkCore;
using WorkJournal.Web.Models;
namespace WorkJournal.Web.Data;

public class JournalDbContext(DbContextOptions<JournalDbContext> options) : DbContext(options)
{
    public DbSet<WorkLog> WorkLogs => Set<WorkLog>();
    public DbSet<PaperTradingAccount> PaperTradingAccounts => Set<PaperTradingAccount>();
    public DbSet<PaperOrder> PaperOrders => Set<PaperOrder>();
    public DbSet<PaperTrade> PaperTrades => Set<PaperTrade>();
    public DbSet<PaperPosition> PaperPositions => Set<PaperPosition>();
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        PaperTradingMapping.Configure(modelBuilder);
        modelBuilder.Entity<WorkLog>().Property(x => x.Hours).HasPrecision(5, 2);
        modelBuilder.Entity<WorkLog>().HasIndex(x => x.WorkDate);
        modelBuilder.Entity<WorkLog>().HasIndex(x => x.Status);
    }
}

