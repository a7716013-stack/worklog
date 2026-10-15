using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WorkJournal.Web.Services;
namespace WorkJournal.Web.Controllers;
public partial class StockAnalysisController
{
    [Authorize, HttpGet, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> PaperQuotes([FromServices] IPaperTradingService paper, CancellationToken cancellationToken, bool refresh = false)
    {
        var p = await paper.GetPortfolioAsync(cancellationToken, refreshDaily: refresh);
        return Json(new { p.TotalAssets, p.TotalMarketValue, p.UnrealizedProfitLoss, p.TotalProfitLoss, p.TotalReturnPercent,
            cash = p.Account.Cash, p.HasIncompleteValuation,
            positions = p.Positions.Select(x => new { symbol = x.Position.StockId, x.CurrentPrice, x.MarketValue, x.UnrealizedProfitLoss, x.ReturnPercent, x.QuoteStatus, x.QuoteTime, date = x.Quote?.TradeDate }) });
    }
}
