using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using WorkJournal.Web.Models;

namespace WorkJournal.Web.Services;

public record StockLookup(StockQuote? Quote, string Message);

public class FinMindStockService(HttpClient client, IMemoryCache cache, IConfiguration config,
    ILogger<FinMindStockService> logger)
{
    private static readonly SemaphoreSlim Gate = new(1, 1);

    public async Task<StockLookup> GetQuoteAsync(string symbol, CancellationToken cancellationToken)
    {
        var key = "finmind:quote:" + symbol;
        if (cache.TryGetValue<StockLookup>(key, out var hit)) return hit!;
        await Gate.WaitAsync(cancellationToken);
        try
        {
            if (cache.TryGetValue<StockLookup>(key, out hit)) return hit!;
            StockLookup result;
            try
            {
                var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(8)).DateTime);
                var infoKey = "finmind:info:" + symbol;
                if (!cache.TryGetValue<JsonElement[]>(infoKey, out var info))
                {
                    info = await ReadAsync("TaiwanStockInfo", symbol, null, null, cancellationToken);
                    cache.Set(infoKey, info, TimeSpan.FromHours(24));
                }
                var name = info!.Where(x => Text(x, "stock_id") == symbol)
                    .OrderByDescending(x => Text(x, "date")).FirstOrDefault();
                if (name.ValueKind == JsonValueKind.Undefined)
                    result = new(null, "查無此台股代號，請確認代號後重試。");
                else
                {
                    var rows = await ReadAsync("TaiwanStockPrice", symbol, today.AddDays(-90), today, cancellationToken);
                    var latest = rows.Where(x => Text(x, "stock_id") == symbol &&
                            DateOnly.TryParseExact(Text(x, "date"), "yyyy-MM-dd", CultureInfo.InvariantCulture,
                                DateTimeStyles.None, out var date) && date <= today)
                        .OrderByDescending(x => Text(x, "date")).FirstOrDefault();
                    if (latest.ValueKind == JsonValueKind.Undefined)
                        result = new(new StockQuote { Symbol = symbol, Name = Text(name, "stock_name") },
                            "近 90 日沒有可用日行情（可能尚未交易或已暫停交易）。");
                    else
                    {
                        var close = Number(latest, "close");
                        var spread = Number(latest, "spread");
                        var previous = close - spread;
                        result = new(new StockQuote
                        {
                            Symbol = symbol, Name = Text(name, "stock_name"),
                            Industry = Text(name, "industry_category"), Market = Text(name, "type"),
                            TradeDate = DateOnly.ParseExact(Text(latest, "date")!, "yyyy-MM-dd", CultureInfo.InvariantCulture),
                            CurrentPrice = close, OpenPrice = Number(latest, "open"),
                            HighPrice = Number(latest, "max"), LowPrice = Number(latest, "min"),
                            ChangePercent = previous > 0 ? spread / previous * 100m : null,
                            Volume = latest.TryGetProperty("Trading_Volume", out var volume) && volume.TryGetInt64(out var count) ? count : null
                        }, "已載入 FinMind 最新可用日行情。");
                    }
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                result = new(null, "行情服務回應逾時，請稍後重試。");
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidDataException)
            {
                logger.LogWarning("FinMind lookup failed ({ErrorType}).", ex.GetType().Name);
                result = new(null, "行情服務暫時無法使用，可能已達查詢額度或授權失效，請稍後重試。");
            }
            cache.Set(key, result, result.Quote?.TradeDate != null ? TimeSpan.FromMinutes(5) : TimeSpan.FromMinutes(1));
            return result;
        }
        finally { Gate.Release(); }
    }

    private async Task<JsonElement[]> ReadAsync(string dataset, string symbol, DateOnly? from, DateOnly? to,
        CancellationToken cancellationToken)
    {
        var url = $"data?dataset={dataset}&data_id={Uri.EscapeDataString(symbol)}";
        if (from.HasValue) url += $"&start_date={from:yyyy-MM-dd}&end_date={to:yyyy-MM-dd}";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        var token = config["FinMind:Token"];
        if (!string.IsNullOrWhiteSpace(token))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Trim());
        using var response = await client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        var root = json.RootElement;
        if (!root.TryGetProperty("status", out var status) || !status.TryGetInt32(out var code) || code != 200 ||
            !root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("Invalid provider response.");
        return data.EnumerateArray().Select(x => x.Clone()).ToArray();
    }

    private static string? Text(JsonElement row, string field) =>
        row.TryGetProperty(field, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static decimal? Number(JsonElement row, string field) =>
        row.TryGetProperty(field, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number) ? number : null;
}
