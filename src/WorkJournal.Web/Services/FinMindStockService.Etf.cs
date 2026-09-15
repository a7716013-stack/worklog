using System.Text.Json;
using WorkJournal.Web.Models;
namespace WorkJournal.Web.Services;
public partial class FinMindStockService
{
    public static bool IsEtfCategory(string? category) => category?.Contains("ETF", StringComparison.OrdinalIgnoreCase) == true;
    private async Task<EtfData> GetEtfAsync(string symbol, string name, DateOnly today, CancellationToken ct)
    {
        var data = etfs == null ? new EtfData() : await etfs.GetAsync(symbol, name, today, ct);
        try
        {
            var rows = await ReadAsync("TaiwanStockDividend", symbol, today.AddYears(-3), today, ct);
            data.Distributions = ParseDistributions(rows, symbol);
            if (data.Distributions.Count > 0) data.DistributionMessage = "FinMind 最新公告紀錄；配股欄為來源配股金額，基金分割不計入配股。";
        }
        catch (Exception ex) when (OptionalFailure(ex, ct))
        { data.DistributionMessage = "配息／配股服務暫時無法使用，請稍後重試。"; }
        return data;
    }
    public static List<EtfDistribution> ParseDistributions(IEnumerable<JsonElement> rows, string symbol) =>
        rows.Where(x => Text(x, "stock_id") == symbol)
        .Select(x => new { Row=x, Date=DateOnly.TryParse(Text(x, "CashExDividendTradingDate"), out var d) ? (DateOnly?)d : DateOnly.TryParse(Text(x, "StockExDividendTradingDate"), out d) ? d : null })
        .Where(x => x.Date != null).GroupBy(x => x.Date!.Value)
        .Select(g => g.OrderByDescending(x => Text(x.Row, "date")).First())
        .OrderByDescending(x => x.Date).Take(6)
        .Select(x => new EtfDistribution(x.Date!.Value,
            Number(x.Row, "CashEarningsDistribution") + Number(x.Row, "CashStatutorySurplus"),
            Number(x.Row, "StockEarningsDistribution") + Number(x.Row, "StockStatutorySurplus"),
            Text(x.Row, "CashDividendPaymentDate"))).ToList();
}
