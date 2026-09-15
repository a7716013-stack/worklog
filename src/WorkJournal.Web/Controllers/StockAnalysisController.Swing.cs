using Microsoft.AspNetCore.Mvc;
using WorkJournal.Web.Models;
using WorkJournal.Web.Services;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace WorkJournal.Web.Controllers;
public partial class StockAnalysisController
{
    private static bool ValidSwingSymbol(string? symbol) => symbol!=null && Regex.IsMatch(symbol,@"^[0-9]{4}[0-9A-Z]{0,2}$");
    [HttpGet]
    public async Task<IActionResult> SwingSearch(string? q, CancellationToken cancellationToken)
    {
        q=q?.Trim();
        if(string.IsNullOrEmpty(q)) return Json(Array.Empty<SwingStock>());
        if(q.Length>30) return BadRequest(new { message="請輸入 30 字以內的股票名稱或代號。" });
        try
        {
            var catalog=await stocks.SwingCatalogAsync(cancellationToken);
            var term=q.Replace("臺","台");
            return Json(catalog.Where(x=>x.Symbol.Contains(term,StringComparison.OrdinalIgnoreCase) || x.Name.Replace("臺","台").Contains(term,StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(x=>x.Symbol.Equals(q,StringComparison.OrdinalIgnoreCase)).ThenBy(x=>x.Symbol).Take(12));
        }
        catch(Exception ex) when(SwingFailure(ex,cancellationToken))
        { return StatusCode(503,new { message="股票搜尋服務暫時無法使用，請稍後重試。" }); }
    }
    [HttpGet]
    public async Task<IActionResult> SwingData(string? symbol, CancellationToken cancellationToken)
    {
        symbol=symbol?.Trim().ToUpperInvariant();
        if(!ValidSwingSymbol(symbol)) return BadRequest(new { message="台股代號格式不正確。" });
        try
        {
            var history=await stocks.SwingHistoryAsync(symbol!,false,cancellationToken);
            return Json(new SwingAnalysis(history.Stock,SwingCalculator.Calculate(history.Bars,history.Flows),SwingCalculator.Chart(history.Bars),history.Message));
        }
        catch(Exception ex) when(SwingFailure(ex,cancellationToken))
        { return StatusCode(503,new { message="波段資料暫時無法取得，可能查無代號或已達查詢額度。請稍後重試。" }); }
    }
    [HttpGet]
    public async Task<IActionResult> SwingBacktest(string? symbol, CancellationToken cancellationToken)
    {
        symbol=symbol?.Trim().ToUpperInvariant();
        if(!ValidSwingSymbol(symbol)) return BadRequest(new { message="台股代號格式不正確。" });
        try
        {
            return Json(await stocks.SwingBacktestAsync(symbol!,cancellationToken));
        }
        catch(Exception ex) when(SwingFailure(ex,cancellationToken))
        { return StatusCode(503,new { message="回測資料暫時無法取得，請稍後重試。" }); }
    }
    private static bool SwingFailure(Exception ex,CancellationToken ct) =>
        ex is HttpRequestException or JsonException or InvalidDataException ||
        ex is OperationCanceledException && !ct.IsCancellationRequested;
}
