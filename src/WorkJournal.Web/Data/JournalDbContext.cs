using Microsoft.EntityFrameworkCore;
using WorkJournal.Web.Models;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
namespace WorkJournal.Web.Data;

public class JournalDbContext(DbContextOptions<JournalDbContext> options) : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<WorkLog> WorkLogs => Set<WorkLog>();
    public DbSet<StockWatchlistItem> StockWatchlistItems => Set<StockWatchlistItem>();
    public DbSet<CalendarSyncLink> CalendarSyncLinks => Set<CalendarSyncLink>();
    public DbSet<GoogleCalendarConnection> GoogleCalendarConnections => Set<GoogleCalendarConnection>();
    public DbSet<PaperTradingAccount> PaperTradingAccounts => Set<PaperTradingAccount>();
    public DbSet<PaperOrder> PaperOrders => Set<PaperOrder>();
    public DbSet<PaperTrade> PaperTrades => Set<PaperTrade>();
    public DbSet<PaperPosition> PaperPositions => Set<PaperPosition>();
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<StockWatchlistItem>().HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.ApplicationUserId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<StockWatchlistItem>().HasIndex(x => new { x.ApplicationUserId, x.Symbol }).IsUnique();
        modelBuilder.Entity<CalendarSyncLink>().HasIndex(x => new { x.ApplicationUserId, x.GoogleEventId }).IsUnique();
        modelBuilder.Entity<CalendarSyncLink>().HasIndex(x => x.WorkLogId).IsUnique().HasFilter("[WorkLogId] IS NOT NULL");
        modelBuilder.Entity<CalendarSyncLink>().HasOne(x => x.WorkLog).WithMany().HasForeignKey(x => x.WorkLogId).OnDelete(DeleteBehavior.SetNull);
        modelBuilder.Entity<CalendarSyncLink>().HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.ApplicationUserId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<WorkLog>().HasOne(x => x.User).WithMany(x => x.WorkLogs)
            .HasForeignKey(x => x.ApplicationUserId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<WorkLog>().HasIndex(x => new { x.ApplicationUserId, x.WorkDate });
        modelBuilder.Entity<GoogleCalendarConnection>().HasOne(x => x.User)
            .WithOne(x => x.GoogleCalendarConnection).HasForeignKey<GoogleCalendarConnection>(x => x.ApplicationUserId)
            .OnDelete(DeleteBehavior.Cascade);
        PaperTradingMapping.Configure(modelBuilder);
        modelBuilder.Entity<WorkLog>().Property(x => x.Hours).HasPrecision(5, 2);
        modelBuilder.Entity<WorkLog>().HasIndex(x => x.WorkDate);
        modelBuilder.Entity<WorkLog>().HasIndex(x => x.Status);
    }
}

