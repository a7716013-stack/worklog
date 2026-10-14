using System.Text.Json;
namespace WorkJournal.Web.Services;
public partial class FinMindStockService
{
    public Task<JsonElement[]> StrategyDatasetAsync(string dataset, string symbol, DateOnly? from, DateOnly? to, CancellationToken ct)
    {
        if (dataset is not ("TaiwanStockTradingDate" or "TaiwanStockPrice" or "TaiwanStockInstitutionalInvestorsBuySell" or "TaiwanStockMonthRevenue"))
            throw new ArgumentException("Unsupported strategy dataset.");
        return ReadAsync(dataset, symbol, from, to, ct);
    }
}
