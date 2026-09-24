using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using WorkJournal.Web.Models;
namespace WorkJournal.Web.Services;
public partial class FinMindStockService
{
    // Same client, Bearer header, parser, cache and gate; no fundamentals calls for valuation.
    public async Task<StockLookup> GetPaperQuoteAsync(string symbol, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(8));
        var today = DateOnly.FromDateTime(now.DateTime);
        var completedThrough = now.Hour >= 14 ? today : today.AddDays(-1);
        var key = $"finmind:paper:{symbol}:{completedThrough}";
        if (cache.TryGetValue<StockLookup>(key, out var hit)) return hit!;
        await Gate.WaitAsync(ct);
        try
        {
            if (cache.TryGetValue<StockLookup>(key, out hit)) return hit!;
            StockLookup result;
            try
            {
                var infoKey = "finmind:info:" + symbol;
                if (!cache.TryGetValue<JsonElement[]>(infoKey, out var info))
                {
                    info = await ReadAsync("TaiwanStockInfo", symbol, null, null, ct);
                    cache.Set(infoKey, info, TimeSpan.FromHours(24));
                }
                var stock = info!.Where(x => Text(x, "stock_id") == symbol && Text(x, "type") is "twse" or "tpex")
                    .OrderByDescending(ParseDate).FirstOrDefault();
                if (stock.ValueKind == JsonValueKind.Undefined) result = new(null, "查無此上市櫃股票。");
                else
                {
                    var rows = await ReadAsync("TaiwanStockPrice", symbol, today.AddDays(-365), completedThrough, ct);
                    var row = rows.Where(x => Text(x, "stock_id") == symbol && ParseDate(x) is DateOnly d && d <= completedThrough && d >= today.AddDays(-365))
                        .OrderByDescending(ParseDate).FirstOrDefault();
                    if (row.ValueKind == JsonValueKind.Undefined || Number(row, "close") is not > 0 || Number(row, "close") > 1000000)
                        result = new(null, "沒有有效的已完成日行情，暫不接受虛擬委託。");
                    else result = new(new StockQuote {
                        Symbol = symbol, Name = Text(stock, "stock_name") ?? symbol,
                        IsEtf = IsEtfCategory(Text(stock, "industry_category")), TradeDate = ParseDate(row),
                        CurrentPrice = Number(row, "close"), OpenPrice = Number(row, "open"),
                        HighPrice = Number(row, "max"), LowPrice = Number(row, "min")
                    }, "FinMind 最新可用已完成日行情，非盤中即時報價。");
                }
            }
            catch (Exception ex) when (OptionalFailure(ex, ct))
            {
                logger.LogWarning("Paper quote unavailable ({ErrorType}).", ex.GetType().Name);
                result = new(null, "部分行情暫時無法取得，請稍後重試。");
            }
            cache.Set(key, result, TimeSpan.FromMinutes(result.Quote == null ? 1 : 5));
            return result;
        }
        finally { Gate.Release(); }
    }
}
