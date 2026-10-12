using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WorkJournal.Web.Data;
using WorkJournal.Web.Services;
using WorkJournal.Web.Security;
namespace WorkJournal.Web.Controllers;
public partial class StockAnalysisController
{
    [Authorize,HttpGet,ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
    public async Task<IActionResult> FavoriteItems([FromServices] StockFavoriteService favorites,[FromServices] ICurrentUser user,CancellationToken ct)
        => Json(new{owner=user.Id,items=await favorites.GetAsync(ct)});
    [Authorize, HttpPost, ResponseCache(Duration=0, Location=ResponseCacheLocation.None, NoStore=true)]
    public async Task<IActionResult> SaveFavorites([FromBody] WatchlistChange input, [FromServices] StockFavoriteService watch,
        [FromServices] ICurrentUser user, CancellationToken ct)
    {
        if (!ModelState.IsValid || input.Symbols is null || input.Symbols.Length > 100 ||
            input.Symbols.Any(x => !ValidSwingSymbol(x)) || (input.Remove is not null && !ValidSwingSymbol(input.Remove)))
            return BadRequest(new { message="請提供有效的台股代號，最多 100 檔。" });
        try
        {
            var catalog = input.Symbols.Length == 0 ? [] : await stocks.SwingCatalogAsync(ct);
            var selected = catalog.Where(x => input.Symbols.Contains(x.Symbol)).ToList();
            if (selected.Count != input.Symbols.Distinct().Count()) return BadRequest(new { message="部分股票代號不存在，清單尚未變更。" });
            return Json(new { owner=user.Id, items=await watch.ChangeAsync(selected, input.Remove, ct) });
        }
        catch (ValidationException ex) { return BadRequest(new { message=ex.Message }); }
        catch (Exception ex) when (SwingFailure(ex,ct)) { return StatusCode(503,new { message="股票資料暫時無法取得，追蹤清單尚未變更，請稍後重試。" }); }
    }

    [Authorize,HttpGet,ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
    public async Task<IActionResult> Favorites([FromServices] StockFavoriteService favorites,CancellationToken ct)
        => View(await favorites.GetAsync(ct));
    [Authorize,HttpPost]
    public async Task<IActionResult> ChangeFavorite(string symbol,bool remove,[FromServices] StockFavoriteService favorites,CancellationToken ct)
    {
        if(!ModelState.IsValid||!ValidSwingSymbol(symbol))return BadRequest();
        try {
            if(remove) { await favorites.ChangeAsync([],symbol,ct); TempData["Success"]="已移除自選，波段名單與交易紀錄保留。"; }
            else {
                var stock=(await stocks.SwingCatalogAsync(ct)).SingleOrDefault(x=>x.Symbol==symbol);
                if(stock==null)throw new ValidationException("找不到股票代號。");
                await favorites.ChangeAsync([stock],null,ct);TempData["Success"]="已加入自選清單；重複加入不會新增紀錄。";
            }
        } catch(ValidationException e){TempData["Warning"]=e.Message;}
        catch(Exception e) when(SwingFailure(e,ct)){TempData["Warning"]="自選資料來源暫時無法取得，請稍後重試。";}
        return RedirectToAction(nameof(Favorites));
    }
    [Authorize,HttpGet,ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
    public async Task<IActionResult> StockMembership(string symbol,[FromServices] JournalDbContext db,[FromServices] ICurrentUser user,CancellationToken ct)
    {
        if(!ValidSwingSymbol(symbol))return BadRequest();
        var owner=user.Id;
        var favorite=await db.StockFavoriteItems.AnyAsync(x=>x.ApplicationUserId==owner&&x.Symbol==symbol,ct);
        var swing=await db.StockWatchlistItems.AnyAsync(x=>x.ApplicationUserId==owner&&x.Symbol==symbol,ct);
        var tracking=await db.MarketRadarPersonalTrackings.AnyAsync(x=>x.ApplicationUserId==owner&&x.StoppedAt==null&&x.Recommendation.StockId==symbol,ct);
        var held=await db.PaperPositions.AnyAsync(x=>db.PaperTradingAccounts.Any(a=>a.Id==x.AccountId&&a.ApplicationUserId==owner)&&x.StockId==symbol&&x.Quantity>0,ct);
        return Json(new{favorite,swing,tracking,held});
    }
    [Authorize,HttpGet,ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
    public async Task<IActionResult> StockPerformance(string symbol,[FromServices] JournalDbContext db,[FromServices] ICurrentUser user,CancellationToken ct)
    {
        if(!ValidSwingSymbol(symbol))return BadRequest();
        var owner=user.Id;
        var recommendations=await db.MarketRadarRecommendations.AsNoTracking().Where(x=>x.StockId==symbol&&db.MarketRadarPersonalTrackings.Any(t=>t.ApplicationUserId==owner&&t.RecommendationId==x.Id))
            .OrderByDescending(x=>x.TradeDate).ThenByDescending(x=>x.Id).Take(20)
            .Select(x=>new{x.TradeDate,x.Score,R1=x.Performance==null?null:x.Performance.Return1D,R5=x.Performance==null?null:x.Performance.Return5D,R20=x.Performance==null?null:x.Performance.Return20D}).ToListAsync(ct);
        var orders=await db.PaperOrders.AsNoTracking().Where(x=>x.StockId==symbol&&db.PaperTradingAccounts.Any(a=>a.Id==x.AccountId&&a.ApplicationUserId==owner))
            .OrderByDescending(x=>x.Id).Take(20).Select(x=>new{Date=x.CreatedAt,Side=x.Side.ToString(),x.Quantity,Status=x.Status.ToString()}).ToListAsync(ct);
        var realizedProfit=await db.PaperTrades.Where(x=>x.StockId==symbol&&db.PaperTradingAccounts.Any(a=>a.Id==x.AccountId&&a.ApplicationUserId==owner)).SumAsync(x=>(decimal?)x.RealizedProfitLoss,ct)??0;
        return Json(new{recommendations,orders,realizedProfit});
    }
    [Authorize,HttpPost]
    public async Task<IActionResult> TrackStock(string symbol,[FromServices] MarketRadarRecommendationService service,
        [FromServices] IMarketRadarHistoryProvider history,[FromServices] ICurrentUser user,CancellationToken ct)
    {
        if(!ModelState.IsValid||!ValidSwingSymbol(symbol))return BadRequest();
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct);timeout.CancelAfter(TimeSpan.FromSeconds(45));
        try {
            await service.FollowStockAsync(user.Id,symbol,history,timeout.Token);
            TempData["Success"]="已加入績效追蹤，保留既有快照與起算日。";
            return RedirectToAction(nameof(RadarPerformance),new{Mine=true,Symbol=symbol,Basis="open"});
        } catch(ValidationException e){TempData["Warning"]=e.Message;}
        catch(Exception e) when(!ct.IsCancellationRequested&&(TaiwanMarketRankingProvider.Failure(e,ct)||e is OperationCanceledException))
        {TempData["Warning"]="行情來源暫時無法取得，尚未加入績效追蹤。";}
        return RedirectToAction(nameof(Index),new{symbol});
    }
}
