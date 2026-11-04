using WorkJournal.Web.Models;
namespace WorkJournal.Web.Services;
public class FinMindRadarHistoryProvider(FinMindStockService stocks) : IMarketRadarHistoryProvider
{
    public Task<RadarHistory> GetAsync(MarketQuote quote,CancellationToken ct) => stocks.RadarHistoryAsync(quote.Stock.Symbol,quote.Date,quote.Stock.IsEtf,ct);
}
