using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using WorkJournal.Web.Models;
using WorkJournal.Web.Services;
using WorkJournal.Web.ViewModels;
namespace WorkJournal.Web.Controllers;
public partial class StockAnalysisController
{
    [HttpGet]
    public async Task<IActionResult> PaperTrading([FromServices] IPaperTradingService paper,
        [FromServices] IOptions<PaperTradingOptions> options, string? symbol, PaperOrderSide side, CancellationToken cancellationToken)
    {
        var portfolio = await paper.GetPortfolioAsync(cancellationToken);
        return View(new PaperTradingViewModel {
            Portfolio = portfolio, Costs = options.Value,
            Order = new() { StockId = ValidSwingSymbol(symbol?.ToUpperInvariant()) ? symbol!.ToUpperInvariant() : "",
                Side = Enum.IsDefined(side) ? side : PaperOrderSide.Buy, AccountGeneration = portfolio.Account.Generation }
        });
    }

    [HttpPost]
    public async Task<IActionResult> PlacePaperOrder([FromServices] IPaperTradingService paper,
        [FromServices] IOptions<PaperTradingOptions> options,
        [Bind(Prefix = "Order")] PaperOrderViewModel input, CancellationToken cancellationToken)
    {
        if (ModelState.IsValid)
        {
            try
            {
                var order = await paper.PlaceOrderAsync(input, cancellationToken);
                TempData["Success"] = order.Status switch {
                    PaperOrderStatus.Filled => "虛擬委託已依最新可用收盤價模擬成交。",
                    PaperOrderStatus.Rejected => "虛擬委託已拒絕：" + order.RejectReason,
                    _ => "虛擬限價委託已建立，等待建立後的新交易日日行情。"
                };
                return RedirectToAction(nameof(PaperTrading), new { symbol = input.StockId });
            }
            catch (ValidationException ex) { ModelState.AddModelError("", ex.Message); }
        }
        var portfolio = await paper.GetPortfolioAsync(cancellationToken);
        return View("PaperTrading", new PaperTradingViewModel { Portfolio = portfolio, Order = input, Costs = options.Value });
    }

    [HttpPost]
    public async Task<IActionResult> CancelPaperOrder([FromServices] IPaperTradingService paper, long id, CancellationToken cancellationToken)
    {
        TempData["Success"] = await paper.CancelOrderAsync(id, cancellationToken)
            ? "虛擬委託已取消。" : "只能取消等待中的虛擬委託。";
        return RedirectToAction(nameof(PaperTrading));
    }

    [HttpPost]
    public async Task<IActionResult> RefreshPaperOrders([FromServices] IPaperTradingService paper, CancellationToken cancellationToken)
    {
        var count = await paper.ExecutePendingOrdersAsync(cancellationToken);
        TempData["Success"] = $"已檢查待成交委託，本次模擬成交 {count} 筆；未有新行情、行情暫不可用或未觸價的委託繼續等待。";
        return RedirectToAction(nameof(PaperTrading));
    }

    [HttpPost]
    public async Task<IActionResult> ResetPaperAccount([FromServices] IPaperTradingService paper,
        Guid generation, bool confirmReset, CancellationToken cancellationToken)
    {
        if (!confirmReset) return BadRequest("請確認重設虛擬帳戶。");
        TempData["Success"] = await paper.ResetAccountAsync(generation, cancellationToken)
            ? "虛擬帳戶已重設，工作日誌未受影響。" : "帳戶已被重設，請重新整理。";
        return RedirectToAction(nameof(PaperTrading));
    }
}
