using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WorkJournal.Web.Data;
using WorkJournal.Web.Models;
namespace WorkJournal.Web.Services;
public class MarketRadarRecommendationService(JournalDbContext db,IMarketRadarService radar,IRadarTrackingDataProvider source,TimeProvider clock)
{
    public static DateOnly TaipeiDate(DateTimeOffset value)=>DateOnly.FromDateTime(value.ToOffset(TimeSpan.FromHours(8)).DateTime);
    public static string Category(MarketRadarStock s)=>s.Score>=70?"Qualified":s.Score is null?"Incomplete":"Supplement";
    public static MarketRadarStock[] SelectDaily(IEnumerable<MarketRadarStock> items)=>items.DistinctBy(x=>x.Quote.Stock.Symbol)
        .OrderBy(x=>Category(x)=="Qualified"?0:Category(x)=="Incomplete"?1:2)
        .ThenByDescending(x=>x.Score??x.Earned).ThenByDescending(x=>x.Available).ThenBy(x=>x.Quote.Stock.Symbol).Take(10).ToArray();
    public async Task<T> Atomic<T>(string key,Func<Task<T>> action,CancellationToken ct)=>await db.Database.CreateExecutionStrategy().ExecuteAsync(async()=>
    {
        db.ChangeTracker.Clear();await using var tx=await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"DECLARE @r int; EXEC @r=sys.sp_getapplock @Resource={key}, @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=15000; IF @r<0 THROW 51003,'Radar busy',1;",ct);
        var result=await action();await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);return result;
    });
    public async Task<RadarCaptureResult> CaptureDailyAsync(CancellationToken ct)
    {
        var now=clock.GetUtcNow();var local=now.ToOffset(TimeSpan.FromHours(8));var day=TaipeiDate(now);
        if(local.TimeOfDay<new TimeSpan(13,30,0))return new(0,0,"尚未收盤，未執行保存。");
        var batch=await db.MarketRadarCaptureBatches.AsNoTracking().SingleOrDefaultAsync(x=>x.TradeDate==day,ct);
        if(batch!=null)return new(0,batch.Count,"今日推薦批次已保存，不改寫既有快照。");
        var basic=await radar.GetAsync("all",day,false,ct);
        if(basic.TradeDate!=day || !new[]{"twse","tpex"}.All(m=>basic.Quotes.Any(x=>x.Date==day&&x.Stock.Market==m)))
            return new(0,0,"休市或當日兩市場收盤資料尚未齊備；不建立批次，稍後再檢查。");
        var data=await radar.GetAsync("all",day,true,ct);
        // Do not freeze an entirely unavailable technical/flow snapshot at 13:30.
        if(data.Analysis.Length==0 || !data.Analysis.Any(x=>x.Technical.MA20.HasValue&&x.ForeignNet.HasValue))
            return new(0,0,"候選歷史／法人資料尚未更新，延後重試。");
        var selected=SelectDaily(data.Analysis.Where(x=>x.Quote.Date==day));
        if(selected.Length==0)return new(0,0,"當日候選尚未齊備，延後重試。");
        var snapshots=new List<MarketRadarRecommendation>();
        foreach(var item in selected)snapshots.Add(await SnapshotAsync(item,data.UpdatedAt,ct));
        return await Atomic("WorkJournal.Radar.Capture",async()=>
        {
            var existing=await db.MarketRadarCaptureBatches.SingleOrDefaultAsync(x=>x.TradeDate==day,ct);
            if(existing!=null)return new RadarCaptureResult(0,existing.Count,"今日批次已存在。");
            db.MarketRadarCaptureBatches.Add(new(){TradeDate=day,Count=snapshots.Count,CreatedAt=clock.GetUtcNow()});
            var rank=0;
            foreach(var snapshot in snapshots)
            {
                var saved=await db.MarketRadarRecommendations.SingleOrDefaultAsync(x=>x.TradeDate==day&&x.StockId==snapshot.StockId,ct);
                if(saved==null){saved=snapshot;db.Add(saved);}
                db.MarketRadarDailySelections.Add(new(){TradeDate=day,Rank=++rank,Recommendation=saved});
            }
            return new RadarCaptureResult(snapshots.Count,0,$"今日已保存 {snapshots.Count} 名（不足 10 檔時不虛構補足）。");
        },ct);
    }
    public async Task<MarketRadarRecommendation> SnapshotAsync(MarketRadarStock s,DateTimeOffset updated,CancellationToken ct)
    {
        RadarResearch research;
        try {research=await source.ResearchAsync(s,ct);}
        catch(Exception e) when(TaiwanMarketRankingProvider.Failure(e,ct)){research=new("未知",null,null,null,null,s.Events);}
        int? Part(int i)=>s.Parts.ElementAtOrDefault(i)?.Points;
        return new(){TradeDate=s.Quote.Date,StockId=s.Quote.Stock.Symbol,StockName=s.Quote.Stock.Name,Market=s.Quote.Stock.Market,IsEtf=s.Quote.Stock.IsEtf,
            Industry=research.Industry,Category=Category(s),ClosePrice=s.Quote.Close,Score=s.Score,EarnedScore=s.Earned,AvailableScore=s.Available,
            MomentumPoints=Part(0),VolumePoints=Part(1),TechnicalPoints=Part(2),ChipPoints=Part(3),FundamentalPoints=Part(4),EventPoints=Part(5),
            ForeignBuyStreak=research.ForeignBuy,ForeignSellStreak=research.ForeignSell,TrustBuyStreak=research.TrustBuy,TrustSellStreak=research.TrustSell,
            SnapshotJson=JsonSerializer.Serialize(s),ResearchJson=JsonSerializer.Serialize(research),CreatedAt=clock.GetUtcNow(),SourceUpdatedAt=updated};
    }
    public async Task<long> FollowAsync(string owner,long? recommendationId,string? symbol,CancellationToken ct)
    {
        MarketRadarRecommendation? pending=null;
        if(recommendationId is null)
        {
            if(symbol is null || !System.Text.RegularExpressions.Regex.IsMatch(symbol,@"^[0-9]{4}[0-9A-Z]{0,2}$"))throw new ValidationException("股票代號不正確。");
            var data=await radar.GetAsync("all",null,true,ct);
            var item=data.Analysis.SingleOrDefault(x=>x.Quote.Stock.Symbol==symbol)??throw new ValidationException("此標的不在最新候選池；可從歷史推薦頁追蹤。");
            pending=await SnapshotAsync(item,data.UpdatedAt,ct);
        }
        return await SaveFollowAsync(owner,recommendationId,pending,null,ct);
    }
    private Task<bool> OwnsSymbolAsync(string owner,string symbol,string origin,CancellationToken ct)
    {
        if(origin=="swing")return db.StockWatchlistItems.AnyAsync(x=>x.ApplicationUserId==owner&&x.Symbol==symbol,ct);
        var accounts=db.PaperTradingAccounts.Where(x=>x.ApplicationUserId==owner).Select(x=>x.Id);
        return db.PaperOrders.AnyAsync(x=>accounts.Contains(x.AccountId)&&x.StockId==symbol,ct);
    }
    public async Task<long> FollowPortfolioSymbolAsync(string owner,string symbol,string origin,IMarketRadarHistoryProvider history,CancellationToken ct)
    {
        if(origin is not ("paper" or "swing") || !System.Text.RegularExpressions.Regex.IsMatch(symbol??"",@"^[0-9]{4}[0-9A-Z]{0,2}$"))
            throw new ValidationException("來源或股票代號不正確。");
        if(!await OwnsSymbolAsync(owner,symbol!,origin,ct))throw new ValidationException("此標的不在你的虛擬委託或波段追蹤名單中。");
        // Full-market quotes include symbols outside the daily candidate pool. Analyze only this symbol.
        var data=await radar.GetAsync("all",null,false,ct);
        var quote=data.Quotes.SingleOrDefault(x=>x.Stock.Symbol==symbol);
        if(quote is null || quote.Close<=0 || quote.Date>TaipeiDate(clock.GetUtcNow()))
            throw new ValidationException("此標的暫無可用收盤行情，尚未加入，請稍後重試。");
        var pending=await db.MarketRadarRecommendations.AsNoTracking().SingleOrDefaultAsync(x=>x.StockId==symbol&&x.TradeDate==quote.Date,ct);
        if(pending==null)
        {
            var bars=await history.GetAsync(quote,ct);
            var item=MarketRadarScoreCalculator.Calculate(quote,bars,data.Events);
            pending=await SnapshotAsync(item,data.UpdatedAt,ct);
        }
        return await SaveFollowAsync(owner,null,pending,()=>OwnsSymbolAsync(owner,symbol!,origin,ct),ct);
    }
    private Task<long> SaveFollowAsync(string owner,long? recommendationId,MarketRadarRecommendation? pending,Func<Task<bool>>? stillOwned,CancellationToken ct)
        => Atomic("WorkJournal.Radar.Capture",async()=>
        {
            if(stillOwned!=null&&!await stillOwned())throw new ValidationException("來源名單已變更，尚未加入，請重新整理。");
            var rec=recommendationId.HasValue?await db.MarketRadarRecommendations.SingleOrDefaultAsync(x=>x.Id==recommendationId&&(db.MarketRadarDailySelections.Any(d=>d.RecommendationId==x.Id)||db.MarketRadarPersonalTrackings.Any(t=>t.RecommendationId==x.Id&&t.ApplicationUserId==owner)),ct):
                await db.MarketRadarRecommendations.SingleOrDefaultAsync(x=>x.TradeDate==pending!.TradeDate&&x.StockId==pending.StockId,ct);
            if(rec==null && pending==null)throw new ValidationException("找不到推薦紀錄。");
            if(rec==null){rec=pending!;db.Add(rec);await db.SaveChangesAsync(ct);}
            var follow=await db.MarketRadarPersonalTrackings.SingleOrDefaultAsync(x=>x.ApplicationUserId==owner&&x.RecommendationId==rec.Id,ct);
            if(follow==null){follow=new(){ApplicationUserId=owner,RecommendationId=rec.Id,JoinedAt=clock.GetUtcNow()};db.Add(follow);}
            // Rejoining preserves the original start and history.
            else follow.StoppedAt=null;
            return rec.Id;
        },ct);
    public async Task<bool> StopAsync(string owner,long id,CancellationToken ct)
    {
        var row=await db.MarketRadarPersonalTrackings.SingleOrDefaultAsync(x=>x.Id==id&&x.ApplicationUserId==owner,ct);
        if(row==null)return false;row.StoppedAt??=clock.GetUtcNow();await db.SaveChangesAsync(ct);return true;
    }
}
