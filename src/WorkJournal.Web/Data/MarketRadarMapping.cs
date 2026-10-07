using Microsoft.EntityFrameworkCore;
using WorkJournal.Web.Models;
namespace WorkJournal.Web.Data;
public static class MarketRadarMapping
{
    public static void Configure(ModelBuilder b)
    {
        var r=b.Entity<MarketRadarRecommendation>();
        r.HasIndex(x=>new{x.TradeDate,x.StockId}).IsUnique();
        r.HasIndex(x=>new{x.TradeDate,x.Category,x.Score});
        r.HasIndex(x=>x.StockId); r.HasIndex(x=>x.Industry);
        b.Entity<MarketRadarCaptureBatch>().HasKey(x=>x.TradeDate);
        var daily=b.Entity<MarketRadarDailySelection>();
        daily.HasIndex(x=>new{x.TradeDate,x.Rank}).IsUnique();
        daily.HasIndex(x=>new{x.TradeDate,x.RecommendationId}).IsUnique();
        daily.HasOne(x=>x.Recommendation).WithMany().HasForeignKey(x=>x.RecommendationId).OnDelete(DeleteBehavior.Restrict);
        daily.HasOne<MarketRadarCaptureBatch>().WithMany().HasForeignKey(x=>x.TradeDate).OnDelete(DeleteBehavior.Cascade);
        var p=b.Entity<MarketRadarPerformance>();
        p.HasIndex(x=>x.RecommendationId).IsUnique();
        p.HasOne(x=>x.Recommendation).WithOne(x=>x.Performance).HasForeignKey<MarketRadarPerformance>(x=>x.RecommendationId).OnDelete(DeleteBehavior.Cascade);
        var personal=b.Entity<MarketRadarPersonalTracking>();
        personal.HasIndex(x=>new{x.ApplicationUserId,x.RecommendationId}).IsUnique();
        personal.HasOne(x=>x.Recommendation).WithMany().HasForeignKey(x=>x.RecommendationId).OnDelete(DeleteBehavior.Restrict);
        personal.HasOne<ApplicationUser>().WithMany().HasForeignKey(x=>x.ApplicationUserId).OnDelete(DeleteBehavior.Cascade);
        var link=b.Entity<MarketRadarPaperTradeLink>();
        link.HasIndex(x=>x.PaperOrderId).IsUnique().HasFilter("[PaperOrderId] IS NOT NULL");
        link.HasOne(x=>x.Recommendation).WithMany().HasForeignKey(x=>x.RecommendationId).OnDelete(DeleteBehavior.Restrict);
        link.HasOne<PaperOrder>().WithMany().HasForeignKey(x=>x.PaperOrderId).OnDelete(DeleteBehavior.SetNull);
        link.HasOne<ApplicationUser>().WithMany().HasForeignKey(x=>x.ApplicationUserId).OnDelete(DeleteBehavior.Restrict);
        foreach(var entity in new[]{typeof(MarketRadarRecommendation),typeof(MarketRadarPerformance)})
            foreach(var property in b.Entity(entity).Metadata.GetProperties().Where(x=>x.ClrType==typeof(decimal)||x.ClrType==typeof(decimal?)))
                b.Entity(entity).Property(property.Name).HasPrecision(28,10);
    }
}
