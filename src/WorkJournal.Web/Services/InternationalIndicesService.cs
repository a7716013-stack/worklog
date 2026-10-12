using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using WorkJournal.Web.Models;

namespace WorkJournal.Web.Services;

public class InternationalIndicesService(HttpClient http, IMemoryCache cache, TimeProvider clock)
{
    public static IReadOnlyList<InternationalIndexDefinition> Definitions { get; } = Array.AsReadOnly(new[] {
        new InternationalIndexDefinition("^DJI", "道瓊工業指數", "美國 · Dow Jones"),
        new InternationalIndexDefinition("^GSPC", "S&P 500", "美國 · 標普500"),
        new InternationalIndexDefinition("^SOX", "費城半導體", "美國 · PHLX Semiconductor"),
        new InternationalIndexDefinition("^IXIC", "那斯達克綜合", "美國 · Nasdaq Composite"),
        new InternationalIndexDefinition("^RUT", "羅素2000", "美國 · Russell 2000"),
        new InternationalIndexDefinition("^N225", "日經225", "日本 · Nikkei 225"),
        new InternationalIndexDefinition("^HSI", "香港恆生", "香港 · Hang Seng")
    });
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private const string SnapshotKey = "international-indices:v1";

    public async Task<InternationalIndicesSnapshot> GetAsync(CancellationToken ct)
    {
        if (cache.TryGetValue<InternationalIndicesSnapshot>(SnapshotKey, out var cached)) return cached!;
        await Gate.WaitAsync(ct);
        try
        {
            if (cache.TryGetValue<InternationalIndicesSnapshot>(SnapshotKey, out cached)) return cached!;
            var quotes = await Task.WhenAll(Definitions.Select(d => FetchAsync(d, ct)));
            var result = new InternationalIndicesSnapshot(quotes, clock.GetUtcNow());
            // A shared five-minute cooldown also applies to provider rate limits and failures.
            cache.Set(SnapshotKey, result, TimeSpan.FromMinutes(5));
            return result;
        }
        finally { Gate.Release(); }
    }

    private async Task<InternationalIndexQuote> FetchAsync(InternationalIndexDefinition definition, CancellationToken ct)
    {
        var key = SnapshotKey + ":last:" + definition.Symbol;
        try
        {
            using var response = await http.GetAsync("https://query1.finance.yahoo.com/v8/finance/chart/"
                + Uri.EscapeDataString(definition.Symbol) + "?interval=1d&range=1d", ct);
            response.EnsureSuccessStatusCode();
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            var quote = Parse(definition.Symbol, document.RootElement, clock.GetUtcNow());
            cache.Set(key, quote, TimeSpan.FromDays(7));
            return quote;
        }
        catch (Exception e) when (e is HttpRequestException or JsonException or InvalidDataException
            or InvalidOperationException or ArgumentOutOfRangeException || e is OperationCanceledException && !ct.IsCancellationRequested)
        {
            if (cache.TryGetValue<InternationalIndexQuote>(key, out var last))
                return last! with { IsStale = true, Status = "更新失敗，顯示上次成功報價" };
            return new(definition.Symbol, null, null, null, null, false, "來源暫時無法取得");
        }
    }

    public static InternationalIndexQuote Parse(string symbol, JsonElement root, DateTimeOffset now)
    {
        if (!root.TryGetProperty("chart", out var chart) || !chart.TryGetProperty("result", out var results)
            || results.ValueKind != JsonValueKind.Array || results.GetArrayLength() != 1
            || !results[0].TryGetProperty("meta", out var meta)) throw new InvalidDataException("Missing index metadata");
        if (!meta.TryGetProperty("symbol", out var actual) || actual.GetString() != symbol
            || !meta.TryGetProperty("instrumentType", out var kind) || kind.GetString() != "INDEX")
            throw new InvalidDataException("Unexpected index symbol or instrument");
        static decimal? Number(JsonElement obj, string name) => obj.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number) ? number : null;
        var price = Number(meta, "regularMarketPrice");
        if (price is null or <= 0 || !meta.TryGetProperty("regularMarketTime", out var time)
            || !time.TryGetInt64(out var seconds)) throw new InvalidDataException("Missing price or timestamp");
        var quotedAt = DateTimeOffset.FromUnixTimeSeconds(seconds);
        if (quotedAt > now.AddMinutes(5) || seconds <= 0) throw new InvalidDataException("Invalid quote timestamp");
        var previous = Number(meta, "previousClose") ?? Number(meta, "chartPreviousClose");
        if (previous <= 0) previous = null;
        return new(symbol, price, previous, quotedAt, now, false, previous == null ? "漲跌基準暫缺" : "最新可用報價");
    }
}
