using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using WorkJournal.Web.Services;

namespace HomeDashboardChecks;

public static class InternationalIndicesChecks
{
    public static async Task RunAsync()
    {
        var count = 0;
        void Check(bool ok, string name) { if (!ok) throw new Exception(name); count++; Console.WriteLine("PASS: " + name); }
        var now = DateTimeOffset.UtcNow;
        JsonElement Meta(object meta) => JsonSerializer.SerializeToElement(new { chart = new { result = new[] { new { meta } } } });
        var up = InternationalIndicesService.Parse("^DJI", Meta(new { symbol = "^DJI", instrumentType = "INDEX", regularMarketPrice = 110, chartPreviousClose = 100, regularMarketTime = now.AddDays(-3).ToUnixTimeSeconds() }), now);
        Check(up.Change == 10 && up.ChangePercent == 10, "global index points and percentage use prior close");
        Check(up.QuotedAt == DateTimeOffset.FromUnixTimeSeconds(now.AddDays(-3).ToUnixTimeSeconds()), "weekend retains actual quote timestamp");
        var down = InternationalIndicesService.Parse("^HSI", Meta(new { symbol = "^HSI", instrumentType = "INDEX", regularMarketPrice = 90, previousClose = 100, chartPreviousClose = 70, regularMarketTime = now.ToUnixTimeSeconds() }), now);
        Check(down.ChangePercent == -10, "explicit previous close takes precedence over chart fallback");
        var noBasis = InternationalIndicesService.Parse("^SOX", Meta(new { symbol = "^SOX", instrumentType = "INDEX", regularMarketPrice = 100, chartPreviousClose = 0, regularMarketTime = now.ToUnixTimeSeconds() }), now);
        Check(noBasis.Value == 100 && noBasis.Change == null && noBasis.ChangePercent == null, "missing comparison not fabricated as zero return");
        foreach (var meta in new object[] {
            new { symbol="^HSI",instrumentType="INDEX",regularMarketPrice=100,regularMarketTime=now.ToUnixTimeSeconds() },
            new { symbol="^DJI",instrumentType="ETF",regularMarketPrice=100,regularMarketTime=now.ToUnixTimeSeconds() },
            new { symbol="^DJI",instrumentType="INDEX",regularMarketPrice=0,regularMarketTime=now.ToUnixTimeSeconds() },
            new { symbol="^DJI",instrumentType="INDEX",regularMarketPrice=100,regularMarketTime=now.AddDays(1).ToUnixTimeSeconds() }
        }) {
            try { InternationalIndicesService.Parse("^DJI", Meta(meta), now); throw new Exception("Invalid data accepted"); }
            catch (InvalidDataException) { Check(true, "invalid instrument, symbol, price or future timestamp rejected"); }
        }
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var handler = new InternationalQuoteHandler();
        var service = new InternationalIndicesService(new HttpClient(handler), cache, TimeProvider.System);
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => service.GetAsync(default)));
        Check(handler.Calls == 7, "concurrent visitors share one request per index");
        Check(results[0].Quotes.Select(x => x.Symbol).SequenceEqual(new[] { "^DJI", "^GSPC", "^SOX", "^IXIC", "^RUT", "^N225", "^HSI" }), "seven exact index symbols in requested order");
        Check(results[0].Quotes.All(x => x.Value > 0), "all valid source quotes displayed");
        handler.FailedSymbol = "^SOX"; cache.Remove("international-indices:v1");
        var partial = await service.GetAsync(default);
        Check(partial.Quotes.Single(x => x.Symbol == "^SOX").IsStale && partial.Quotes.Single(x => x.Symbol == "^SOX").QuotedAt == results[0].Quotes.Single(x => x.Symbol == "^SOX").QuotedAt, "rate limit preserves last good price and original timestamp");
        Check(partial.Quotes.Count(x => !x.IsStale && x.Value > 0) == 6, "single index failure preserves other six");
        var calls = handler.Calls; await service.GetAsync(default);
        Check(handler.Calls == calls, "failure cooldown prevents immediate repeated upstream requests");
        cache.Remove("international-indices:v1"); cache.Remove("international-indices:v1:last:^SOX");
        var missing = (await service.GetAsync(default)).Quotes.Single(x => x.Symbol == "^SOX");
        Check(missing.Value == null && missing.ChangePercent == null && missing.QuotedAt == null, "first fetch failure produces missing values, not fake quote");
        Console.WriteLine($"InternationalIndicesChecks: {count} PASS");
    }
}

public class InternationalQuoteHandler : HttpMessageHandler
{
    public int Calls;
    public string? FailedSymbol;
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Interlocked.Increment(ref Calls); await Task.Delay(5, ct);
        var symbol = Uri.UnescapeDataString(request.RequestUri!.Segments.Last());
        if (symbol == FailedSymbol) return new(HttpStatusCode.TooManyRequests);
        var price = symbol switch { "^DJI" => 41000m, "^GSPC" => 5500m, "^SOX" => 4900m, "^IXIC" => 18000m, "^RUT" => 2200m, "^N225" => 38000m, _ => 20000m };
        var meta = new { symbol, instrumentType = "INDEX", regularMarketPrice = price, chartPreviousClose = symbol == "^SOX" ? price + 100 : price - 100, regularMarketTime = DateTimeOffset.UtcNow.AddHours(-2).ToUnixTimeSeconds() };
        return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { chart = new { result = new[] { new { meta } } } })) };
    }
}
