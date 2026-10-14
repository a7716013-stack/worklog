using Microsoft.EntityFrameworkCore;
using WorkJournal.Web.Models;
namespace WorkJournal.Web.Data;
public static class StrategyMapping
{
    public static void Configure(ModelBuilder m)
    {
        m.Entity<ScanProfile>().HasIndex(x => new { x.Code, x.Version }).IsUnique();
        m.Entity<StrategyScanRun>().HasOne<ScanProfile>().WithMany().HasForeignKey(x => x.ProfileId).OnDelete(DeleteBehavior.Restrict);
        m.Entity<StrategyScanRun>().HasIndex(x => new { x.TradeDate, x.ProfileId });
        m.Entity<StrategySourceCache>();
        m.Entity<StrategyDailyStock>().HasIndex(x => new { x.ProfileId, x.TradeDate, x.Symbol }).IsUnique();
        m.Entity<StrategyEpisode>().HasOne<ScanProfile>().WithMany().HasForeignKey(x => x.ProfileId).OnDelete(DeleteBehavior.Restrict);
        m.Entity<StrategyEpisode>().HasIndex(x => new { x.ProfileId, x.Symbol, x.StrategyCode, x.StrategyVersion }).IsUnique().HasFilter("[EndedTradeDate] IS NULL");
        m.Entity<StrategyEvaluation>().HasIndex(x => new { x.ProfileId, x.TradeDate, x.Symbol, x.StrategyCode, x.StrategyVersion }).IsUnique();
        m.Entity<StrategyEvaluation>().HasOne<StrategyScanRun>().WithMany().HasForeignKey(x => x.RunId).OnDelete(DeleteBehavior.Restrict);
        m.Entity<StrategySignal>().HasIndex(x => new { x.ProfileId, x.TradeDate, x.Symbol, x.StrategyCode, x.StrategyVersion }).IsUnique();
        m.Entity<StrategySignal>().HasOne(x => x.Episode).WithMany().HasForeignKey(x => x.EpisodeId).OnDelete(DeleteBehavior.Restrict);
        m.Entity<StrategySignal>().HasOne(x => x.Snapshot).WithOne().HasForeignKey<StrategySignalSnapshot>(x => x.StrategySignalId).OnDelete(DeleteBehavior.Restrict);
        m.Entity<StrategySignal>().HasOne(x => x.Performance).WithOne().HasForeignKey<StrategySignalPerformance>(x => x.StrategySignalId).OnDelete(DeleteBehavior.Restrict);
        m.Entity<PaperOrder>().HasOne<StrategySignal>().WithMany().HasForeignKey(x => x.StrategySignalId).OnDelete(DeleteBehavior.Restrict);
        foreach (var t in new[] { typeof(ScanProfile), typeof(StrategySignalSnapshot), typeof(StrategySignalPerformance) })
            foreach (var p in m.Entity(t).Metadata.GetProperties())
                if ((Nullable.GetUnderlyingType(p.ClrType) ?? p.ClrType) == typeof(decimal)) { p.SetPrecision(28); p.SetScale(8); }
    }
    public static void Protect(JournalDbContext db)
    {
        if (db.ChangeTracker.Entries().Any(x => (x.Entity is StrategySignalSnapshot or StrategySignal) && x.State is EntityState.Modified or EntityState.Deleted))
            throw new InvalidOperationException("策略訊號與快照不可覆寫。");
        foreach (var e in db.ChangeTracker.Entries<ScanProfile>().Where(x => x.State == EntityState.Modified))
            if (e.Properties.Any(x => x.IsModified && x.Metadata.Name != nameof(ScanProfile.IsActive)))
                throw new InvalidOperationException("研究參數必須建立新版本。");
    }
}
