using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using WorkJournal.Web.Data;
using WorkJournal.Web.Models;
using WorkJournal.Web.ViewModels;
namespace WorkJournal.Web.Services;
public class RadarPaperTradingComparisonService(JournalDbContext db,MarketRadarRecommendationService atomic,TimeProvider clock)
{
    public async Task<MarketRadarRecommendation?> AccessibleAsync(long id,string owner,CancellationToken ct)=>await db.MarketRadarRecommendations.AsNoTracking().Include(x=>x.Performance)
        .SingleOrDefaultAsync(x=>x.Id==id&&(db.MarketRadarDailySelections.Any(d=>d.RecommendationId==id)||db.MarketRadarPersonalTrackings.Any(t=>t.RecommendationId==id&&t.ApplicationUserId==owner)),ct);
    public async Task ValidateAsync(long id,string symbol,string owner,CancellationToken ct)
    {var rec=await AccessibleAsync(id,owner,ct);if(rec==null||rec.StockId!=symbol)throw new ValidationException("推薦來源不存在、無權限或股票代號不一致。");}
    public async Task LinkAsync(long recId,long orderId,string owner,CancellationToken ct)
    {
        await atomic.Atomic("WorkJournal.Radar.OrderLink",async()=>
        {
            var order=await db.PaperOrders.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==orderId&&db.PaperTradingAccounts.Any(a=>a.Id==x.AccountId&&a.ApplicationUserId==owner),ct)
                ??throw new ValidationException("找不到你的委託；可能已重設帳戶。");
            await ValidateAsync(recId,order.StockId,owner,ct);
            var link=await db.MarketRadarPaperTradeLinks.SingleOrDefaultAsync(x=>x.PaperOrderId==orderId,ct);
            if(link!=null){if(link.RecommendationId!=recId)throw new ValidationException("此委託已有其他推薦來源，不可覆寫。");return 0;}
            db.MarketRadarPaperTradeLinks.Add(new(){ApplicationUserId=owner,RecommendationId=recId,PaperOrderId=orderId,OriginalPaperOrderId=orderId,CreatedAt=clock.GetUtcNow()});return 0;
        },ct);
    }
    public async Task<List<RadarPaperComparison>> CompareAsync(string owner,CancellationToken ct)
    {
        var links=await db.MarketRadarPaperTradeLinks.AsNoTracking().Where(x=>x.ApplicationUserId==owner).Include(x=>x.Recommendation).ThenInclude(x=>x.Performance).OrderByDescending(x=>x.CreatedAt).Take(500).ToListAsync(ct);
        var ids=links.Where(x=>x.PaperOrderId.HasValue).Select(x=>x.PaperOrderId!.Value).ToArray();
        var orders=await db.PaperOrders.AsNoTracking().Where(x=>ids.Contains(x.Id)&&db.PaperTradingAccounts.Any(a=>a.Id==x.AccountId&&a.ApplicationUserId==owner)).ToListAsync(ct);
        var trades=await db.PaperTrades.AsNoTracking().Where(x=>ids.Contains(x.OrderId)&&db.PaperTradingAccounts.Any(a=>a.Id==x.AccountId&&a.ApplicationUserId==owner)).ToListAsync(ct);
        return links.GroupBy(x=>x.RecommendationId).Select(g=>
        {
            var r=g.First().Recommendation;var orderIds=g.Where(x=>x.PaperOrderId.HasValue).Select(x=>x.PaperOrderId!.Value).ToHashSet();
            var sells=trades.Where(x=>orderIds.Contains(x.OrderId)&&x.Side==PaperOrderSide.Sell).ToArray();
            var profit=sells.Sum(x=>x.RealizedProfitLoss);var cost=sells.Sum(x=>x.GrossAmount-x.Commission-x.TransactionTax-x.RealizedProfitLoss);decimal? ret=cost>0?profit/cost*100:null;
            return new RadarPaperComparison(r.Id,r.StockId,r.TradeDate,r.Performance?.Return20D,r.Performance?.OpenReturn20D,g.Count(),orders.Count(x=>orderIds.Contains(x.Id)&&x.Status==PaperOrderStatus.Filled),g.Count(x=>x.PaperOrderId==null),profit,ret,ret-r.Performance?.Return20D);
        }).ToList();
    }
}
