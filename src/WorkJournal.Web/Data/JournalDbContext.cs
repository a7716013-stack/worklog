using Microsoft.EntityFrameworkCore;
using WorkJournal.Web.Models;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
namespace WorkJournal.Web.Data;

public class JournalDbContext(DbContextOptions<JournalDbContext> options) : IdentityDbContext<ApplicationUser>(options)
{
    private void ProtectRadarSnapshots()
    {
        if(ChangeTracker.Entries<MarketRadarRecommendation>().Any(x=>x.State is EntityState.Modified or EntityState.Deleted))
            throw new InvalidOperationException("已保存的推薦快照不可修改或刪除。");
    }
    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {ProtectRadarSnapshots();return base.SaveChanges(acceptAllChangesOnSuccess);}
    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess,CancellationToken cancellationToken=default)
    {ProtectRadarSnapshots();return base.SaveChangesAsync(acceptAllChangesOnSuccess,cancellationToken);}
    public DbSet<MarketRadarRecommendation> MarketRadarRecommendations => Set<MarketRadarRecommendation>();
    public DbSet<MarketRadarCaptureBatch> MarketRadarCaptureBatches => Set<MarketRadarCaptureBatch>();
    public DbSet<MarketRadarDailySelection> MarketRadarDailySelections => Set<MarketRadarDailySelection>();
    public DbSet<MarketRadarPerformance> MarketRadarPerformances => Set<MarketRadarPerformance>();
    public DbSet<MarketRadarPersonalTracking> MarketRadarPersonalTrackings => Set<MarketRadarPersonalTracking>();
    public DbSet<MarketRadarPaperTradeLink> MarketRadarPaperTradeLinks => Set<MarketRadarPaperTradeLink>();
    public DbSet<MarketPulseDaily> MarketPulseDays => Set<MarketPulseDaily>();
    public DbSet<PortfolioDailyValue> PortfolioDailyValues => Set<PortfolioDailyValue>();
    public DbSet<WorkLog> WorkLogs => Set<WorkLog>();
    public DbSet<StockFavoriteItem> StockFavoriteItems => Set<StockFavoriteItem>();
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
        modelBuilder.Entity<MarketPulseDaily>().HasKey(x=>new{x.TradeDate,x.Market});
        modelBuilder.Entity<PortfolioDailyValue>().HasIndex(x=>new{x.AccountId,x.Generation,x.TradeDate}).IsUnique();
        modelBuilder.Entity<PortfolioDailyValue>().HasOne<PaperTradingAccount>().WithMany().HasForeignKey(x=>x.AccountId).OnDelete(DeleteBehavior.Cascade);
        foreach(var name in new[]{"Cash","MarketValue","TotalAssets","InitialCash"})modelBuilder.Entity<PortfolioDailyValue>().Property<decimal>(name).HasPrecision(28,4);
        modelBuilder.Entity<StockFavoriteItem>().HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.ApplicationUserId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<StockFavoriteItem>().HasIndex(x => new { x.ApplicationUserId, x.Symbol }).IsUnique();
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
        MarketRadarMapping.Configure(modelBuilder);
        modelBuilder.Entity<WorkLog>().Property(x => x.Hours).HasPrecision(5, 2);
        modelBuilder.Entity<WorkLog>().HasIndex(x => x.WorkDate);
        modelBuilder.Entity<WorkLog>().HasIndex(x => x.Status);
    }
}

