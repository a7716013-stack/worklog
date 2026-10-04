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
