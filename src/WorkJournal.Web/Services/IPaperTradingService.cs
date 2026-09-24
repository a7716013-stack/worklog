using WorkJournal.Web.Models;
using WorkJournal.Web.ViewModels;
namespace WorkJournal.Web.Services;
public interface IPaperTradingService
{
    Task<PaperTradingAccount> GetAccountAsync(CancellationToken ct);
    Task<PaperPortfolioViewModel> GetPortfolioAsync(CancellationToken ct);
    Task<PaperOrder> PlaceOrderAsync(PaperOrderViewModel input, CancellationToken ct);
    Task<bool> CancelOrderAsync(long id, CancellationToken ct);
    Task<int> ExecutePendingOrdersAsync(CancellationToken ct);
    Task<List<PaperOrder>> GetOrdersAsync(CancellationToken ct);
    Task<List<PaperTrade>> GetTradesAsync(CancellationToken ct);
    Task<bool> ResetAccountAsync(Guid generation, CancellationToken ct);
}
