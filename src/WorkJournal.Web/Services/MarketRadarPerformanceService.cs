using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WorkJournal.Web.Data;
using WorkJournal.Web.Models;
namespace WorkJournal.Web.Services;
public class MarketRadarPerformanceService(JournalDbContext db,IRadarTrackingDataProvider source,MarketRadarRecommendationService recommendations,TimeProvider clock,ILogger<MarketRadarPerformanceService> logger)
{
    private static RadarEvaluation Merge(RadarEvaluation next,RadarEvaluation? previous)
    {
        if(previous?.Horizons==null)return next;
        if(previous.EntryDate.HasValue&&next.EntryDate.HasValue&&previous.EntryDate!=next.EntryDate)return previous;
        return next with {EntryDate=previous.EntryDate??next.EntryDate,EntryPrice=previous.EntryPrice??next.EntryPrice,
            Horizons=next.Horizons.Select(h=>previous.Horizons.FirstOrDefault(p=>p.Days==h.Days&&p.Return.HasValue)??h).ToArray()};
    }
    public async Task<int> UpdatePendingAsync(CancellationToken ct)
    {
        var now=clock.GetUtcNow();var today=MarketRadarRecommendationService.TaipeiDate(now);
        var through=now.ToOffset(TimeSpan.FromHours(8)).TimeOfDay>=new TimeSpan(18,0,0)?today:today.AddDays(-1);
        var retryBefore=now.AddHours(-1);
        var pending=await db.MarketRadarRecommendations.AsNoTracking().Where(x=>x.TradeDate<through&&(x.Performance==null||((x.Performance.Return20D==null||x.Performance.OpenReturn20D==null)&&x.Performance.UpdatedAt<retryBefore)))
            .OrderBy(x=>x.Performance==null?DateTimeOffset.MinValue:x.Performance.UpdatedAt).ThenBy(x=>x.Id).Take(200).ToListAsync(ct);
        var personal=await db.MarketRadarPersonalTrackings.AsNoTracking().Include(x=>x.Recommendation).Where(x=>!x.Completed&&x.StoppedAt==null&&(x.UpdatedAt==null||x.UpdatedAt<retryBefore))
            .OrderBy(x=>x.UpdatedAt).ThenBy(x=>x.Id).Take(200).ToListAsync(ct);
        var all=pending.Concat(personal.Select(x=>x.Recommendation)).DistinctBy(x=>x.Id).ToArray();var count=0;
        foreach(var group in all.GroupBy(x=>x.StockId))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var from=group.Min(x=>x.TradeDate);var bars=await source.PricesAsync(group.Key,from,through,ct);
                if(bars.Length==0)continue;
                await recommendations.Atomic("WorkJournal.Radar.Performance",async()=>
                {
                    foreach(var r in group)
                    {
                        var close=MarketRadarPerformanceCalculator.Evaluate(bars,r.TradeDate,through,r.ClosePrice);
                        var open=MarketRadarPerformanceCalculator.Evaluate(bars,MarketRadarRecommendationService.TaipeiDate(r.CreatedAt),through);
                        var p=await db.MarketRadarPerformances.SingleOrDefaultAsync(x=>x.RecommendationId==r.Id,ct);
                        if(p==null){p=new(){RecommendationId=r.Id};db.Add(p);}
                        var old=JsonSerializer.Deserialize<Dictionary<string,RadarEvaluation>>(p.DetailJson);
                        close=Merge(close,old?.GetValueOrDefault("Close"));open=Merge(open,old?.GetValueOrDefault("NextOpen"));
                        // Never erase previously observed horizons due to an upstream partial result.
                        var c=close.Horizons;var o=open.Horizons;
                        p.Return1D=c[0].Return??p.Return1D;p.Return3D=c[1].Return??p.Return3D;p.Return5D=c[2].Return??p.Return5D;p.Return10D=c[3].Return??p.Return10D;p.Return20D=c[4].Return??p.Return20D;
                        p.Mfe5D=c[2].Mfe??p.Mfe5D;p.Mae5D=c[2].Mae??p.Mae5D;p.Mfe20D=c[4].Mfe??p.Mfe20D;p.Mae20D=c[4].Mae??p.Mae20D;
                        p.EntryDate=open.EntryDate??p.EntryDate;p.EntryPrice=open.EntryPrice??p.EntryPrice;
                        p.OpenReturn1D=o[0].Return??p.OpenReturn1D;p.OpenReturn3D=o[1].Return??p.OpenReturn3D;p.OpenReturn5D=o[2].Return??p.OpenReturn5D;p.OpenReturn10D=o[3].Return??p.OpenReturn10D;p.OpenReturn20D=o[4].Return??p.OpenReturn20D;
                        p.DetailJson=JsonSerializer.Serialize(new{Close=close,NextOpen=open});p.Status=close.Status;p.EvaluatedThroughTradeDate=bars.Where(x=>x.Date<=through).Max(x=>(DateOnly?)x.Date);p.UpdatedAt=now;
                    }
                    foreach(var original in personal.Where(x=>x.Recommendation.StockId==group.Key))
                    {
                        var item=await db.MarketRadarPersonalTrackings.SingleAsync(x=>x.Id==original.Id,ct);
                        if(item.StoppedAt!=null)continue;
                        var result=Merge(MarketRadarPerformanceCalculator.Evaluate(bars,MarketRadarRecommendationService.TaipeiDate(item.JoinedAt),through),JsonSerializer.Deserialize<RadarEvaluation>(item.PerformanceJson));
                        item.PerformanceJson=JsonSerializer.Serialize(result);item.Completed=result.Horizons.Last().Return.HasValue;item.UpdatedAt=now;
                    }
                    return 0;
                },ct);count+=group.Count();
            }
            catch(Exception e) when(TaiwanMarketRankingProvider.Failure(e,ct) || e is DbUpdateException)
            {logger.LogWarning("Radar performance update skipped {Symbol} ({Type})",group.Key,e.GetType().Name);}
        }
        return count;
    }
}
