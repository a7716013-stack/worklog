using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Caching.Memory;
using WorkJournal.Web.Models;
using WorkJournal.Web.ViewModels;
namespace WorkJournal.Web.Services;

public record PaperTick(string Symbol, decimal Price, DateTimeOffset At);
public class PaperIntradayService(HttpClient client, IMemoryCache cache, TimeProvider clock)
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    public static bool InSession(DateTimeOffset time)
    {
        var local = time.ToOffset(TimeSpan.FromHours(8));
        return local.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday &&
            local.TimeOfDay >= new TimeSpan(9, 0, 0) && local.TimeOfDay <= new TimeSpan(13, 30, 30);
    }
    public static PaperTick[] Parse(string json, DateTimeOffset now)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("msgArray", out var rows) || rows.ValueKind != JsonValueKind.Array) return [];
        var result = new List<PaperTick>();
        foreach (var row in rows.EnumerateArray())
        {
            if (row.ValueKind != JsonValueKind.Object) continue;
            string Field(JsonElement value, string name) => value.TryGetProperty(name, out var field) ? field.ToString() : "";
            var symbol = Field(row, "c"); var date = Field(row, "d");
            if (Field(row, "ip") == "1") continue;
            var priceText = Field(row, "z"); var time = Field(row, "t");
            // A dash is not a trade. Use only the explicit last-trade record, never bid/ask or yesterday close.
            if (!decimal.TryParse(priceText, NumberStyles.Number, CultureInfo.InvariantCulture, out var price) || price <= 0)
            {
                if (!row.TryGetProperty("trade", out var trade)) continue;
                priceText = Field(trade, "z"); time = Field(trade, "t");
                if (!decimal.TryParse(priceText, NumberStyles.Number, CultureInfo.InvariantCulture, out price) || price <= 0) continue;
            }
            if (price > 1000000 || !DateTime.TryParseExact(date + " " + time, "yyyyMMdd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var stamp)) continue;
            var at = new DateTimeOffset(stamp, TimeSpan.FromHours(8));
            if (at.Date != now.ToOffset(TimeSpan.FromHours(8)).Date || at > now.AddSeconds(5) || stamp.TimeOfDay < new TimeSpan(9,0,0) || stamp.TimeOfDay > new TimeSpan(13,30,30)) continue;
            result.Add(new(symbol, price, at));
        }
        return result.ToArray();
    }
    public async Task ApplyAsync(PaperPortfolioViewModel portfolio, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        if (!InSession(now)) return;
        var symbols = portfolio.Positions.Select(x => x.Position.StockId).Where(x => Regex.IsMatch(x, "^[0-9A-Z]{4,6}$")).Distinct().Order().ToArray();
        if (symbols.Length == 0) return;
        var ticks = new List<PaperTick>();
        foreach (var batch in symbols.Chunk(20))
        {
            var key = "paper:intraday:" + now.ToOffset(TimeSpan.FromHours(8)).ToString("yyyyMMdd") + ":" + string.Join(',', batch);
            await Gate.WaitAsync(ct);
            try
            {
                if (!cache.TryGetValue<PaperTick[]>(key, out var cached))
                {
                    try
                    {
                        var channels = string.Join('|', batch.SelectMany(x => new[] { "tse_" + x + ".tw", "otc_" + x + ".tw" }));
                        var json = await client.GetStringAsync("https://mis.twse.com.tw/stock/api/getStockInfo.jsp?ex_ch=" + Uri.EscapeDataString(channels) + "&json=1&delay=0", ct);
                        cached = Parse(json, clock.GetUtcNow());
                    }
                    catch (Exception e) when (e is HttpRequestException or JsonException or TaskCanceledException && !ct.IsCancellationRequested) { cached = []; }
                    cache.Set(key, cached, TimeSpan.FromSeconds(15));
                }
                ticks.AddRange(cached!);
            }
            finally { Gate.Release(); }
        }
        for (var i = 0; i < portfolio.Positions.Count; i++)
        {
            var item = portfolio.Positions[i];
            var tick = ticks.Where(x => x.Symbol == item.Position.StockId).OrderByDescending(x => x.At).FirstOrDefault();
            if (tick == null) { portfolio.Positions[i] = item with { QuoteStatus = "盤中報價未取得／尚無今日成交；日行情參考" }; continue; }
            portfolio.Positions[i] = item with {
                Quote = new StockQuote { Symbol = tick.Symbol, Name = item.Position.StockName, CurrentPrice = tick.Price, TradeDate = DateOnly.FromDateTime(tick.At.DateTime) },
                QuoteTime = tick.At, QuoteStatus = now - tick.At > TimeSpan.FromMinutes(2) ? "最近成交（超過 2 分鐘未更新）" : "盤中最近成交 · 證交所 MIS"
            };
        }
    }
}
