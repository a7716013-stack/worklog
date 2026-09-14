using Microsoft.AspNetCore.Mvc;
using WorkJournal.Web.Models;
using WorkJournal.Web.Services;
using WorkJournal.Web.ViewModels;

namespace WorkJournal.Web.Controllers;

public class StockAnalysisController(FinMindStockService stocks) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index([FromQuery] string? symbol, CancellationToken cancellationToken)
    {
        var model = new StockAnalysisViewModel { Symbol = symbol?.Trim().ToUpperInvariant() };
        if (TryValidateModel(model) && !string.IsNullOrEmpty(model.Symbol))
        {
            var result = await stocks.GetQuoteAsync(model.Symbol, cancellationToken);
            model.Quote = result.Quote ?? new StockQuote { Symbol = model.Symbol };
            model.Message = result.Message;
        }
        return View(model);
    }
}
