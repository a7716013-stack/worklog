using Microsoft.AspNetCore.Mvc;
using WorkJournal.Web.Models;
using WorkJournal.Web.Services;
using WorkJournal.Web.ViewModels;

namespace WorkJournal.Web.Controllers;

public partial class StockAnalysisController(FinMindStockService stocks) : Controller
{
    [Microsoft.AspNetCore.Authorization.Authorize, HttpGet, ResponseCache(Duration=0, Location=ResponseCacheLocation.None, NoStore=true)]
    public IActionResult Swing() => View();

    [HttpGet]
    public async Task<IActionResult> Index([FromQuery] string? symbol, CancellationToken cancellationToken)
    {
        var term=symbol?.Trim();
        var model = new StockAnalysisViewModel { Query=term };
        if(!string.IsNullOrEmpty(term))
        {
            if(term.Length>30){ model.Message="請輸入 30 字以內的股票名稱或代號。";return View(model); }
            if(ValidSwingSymbol(term.ToUpperInvariant())) model.Symbol=term.ToUpperInvariant();
            else try {
                var normalized=term.Replace("臺","台");
                model.Matches=(await stocks.SwingCatalogAsync(cancellationToken))
                    .Where(x=>x.Name.Replace("臺","台").Contains(normalized,StringComparison.OrdinalIgnoreCase)||x.Symbol.Contains(term,StringComparison.OrdinalIgnoreCase))
                    .OrderBy(x=>x.Symbol).Take(30).ToList();
                if(model.Matches.Count==1) return RedirectToAction(nameof(Index),new{symbol=model.Matches[0].Symbol});
                model.Message=model.Matches.Count==0?"查無符合的股票。":"請從搜尋結果選擇股票。";
            } catch(Exception e) when(SwingFailure(e,cancellationToken)) {model.Message="搜尋來源暫時無法取得，請稍後重試。";}
            if(model.Symbol!=null) {
                var result=await stocks.GetQuoteAsync(model.Symbol,cancellationToken);
                model.Quote=result.Quote??new StockQuote{Symbol=model.Symbol};model.Message=result.Message;
            }
        }
        return View(model);
    }
}
