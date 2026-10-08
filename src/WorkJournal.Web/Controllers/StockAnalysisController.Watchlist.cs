using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WorkJournal.Web.Services;
using WorkJournal.Web.Security;
namespace WorkJournal.Web.Controllers;

public partial class StockAnalysisController
{
    [Authorize, HttpGet, ResponseCache(Duration=0, Location=ResponseCacheLocation.None, NoStore=true)]
    public async Task<IActionResult> Watchlist([FromServices] StockWatchlistService watch, [FromServices] ICurrentUser user, CancellationToken ct)
        => Json(new { owner=user.Id, items=await watch.GetAsync(ct) });

    [Authorize, HttpPost]
    public async Task<IActionResult> AddToSwing(string symbol, [FromServices] StockWatchlistService watch, CancellationToken ct)
    {
        if (!ModelState.IsValid || !ValidSwingSymbol(symbol)) return BadRequest();
        try
        {
            var existing = await watch.GetAsync(ct);
            if (existing.Any(x => x.Symbol == symbol))
                TempData["Success"] = $"{symbol} 已在你的波段分析清單中。";
            else
            {
                var stock = (await stocks.SwingCatalogAsync(ct)).SingleOrDefault(x => x.Symbol == symbol);
                if (stock is null) throw new ValidationException("找不到此股票代號，清單尚未變更。");
                await watch.ChangeAsync([stock], null, ct);
                TempData["Success"] = $"{symbol} 已加入你的波段分析清單。";
            }
        }
        catch (ValidationException ex) { TempData["Warning"] = ex.Message; }
        catch (Exception ex) when (SwingFailure(ex, ct))
        { TempData["Warning"] = "股票資料暫時無法取得，請稍後重試。"; }
        return RedirectToAction(nameof(Swing));
    }

    [Authorize, HttpPost, ResponseCache(Duration=0, Location=ResponseCacheLocation.None, NoStore=true)]
    public async Task<IActionResult> SaveWatchlist([FromBody] WatchlistChange input, [FromServices] StockWatchlistService watch,
        [FromServices] ICurrentUser user, CancellationToken ct)
    {
        if (!ModelState.IsValid || input.Symbols is null || input.Symbols.Length > 20 ||
            input.Symbols.Any(x => !ValidSwingSymbol(x)) || (input.Remove is not null && !ValidSwingSymbol(input.Remove)))
            return BadRequest(new { message="請提供有效的台股代號，最多 20 檔。" });
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
}
public class WatchlistChange
{
    public string[] Symbols { get; set; } = [];
    public string? Remove { get; set; }
}
