using Microsoft.AspNetCore.Mvc;
using WorkJournal.Web.Models;
using WorkJournal.Web.ViewModels;

namespace WorkJournal.Web.Controllers;

public class StockAnalysisController : Controller
{
    [HttpGet]
    public IActionResult Index([FromQuery] string? symbol)
    {
        var model = new StockAnalysisViewModel { Symbol = symbol?.Trim().ToUpperInvariant() };
        if (TryValidateModel(model) && !string.IsNullOrEmpty(model.Symbol))
        {
            model.Quote = new StockQuote { Symbol = model.Symbol };
            model.Message = $"已選擇 {model.Symbol}，行情資料尚未接入。";
        }
        return View(model);
    }
}
