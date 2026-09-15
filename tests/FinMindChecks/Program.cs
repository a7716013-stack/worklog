using System.Net;
using System.Text;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using WorkJournal.Web.Services;
using WorkJournal.Web.Models;

static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
var date = DateTime.UtcNow.ToString("yyyy-MM-dd");
var info = """{"status":200,"data":[{"stock_id":"2330","stock_name":"台積電","date":"2026-01-01","type":"twse"}]}""";
var prices = $$"""{"status":200,"data":[{"stock_id":"2330","date":"{{date}}","close":110,"spread":10,"open":101,"max":112,"min":100,"Trading_Volume":123456},{"stock_id":"2330","date":"2000-01-01","close":1,"spread":0}]}""";
async Task<StockLookup> Run(string price, HttpStatusCode status = HttpStatusCode.OK, bool failFundamentals = false)
{
    using var memory = new MemoryCache(new MemoryCacheOptions());
    var handler = new FakeHandler(info, price, status, failFundamentals);
    var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> { ["FinMind:Token"]="test-token" }).Build();
    using var client = new HttpClient(handler) { BaseAddress = new Uri("https://example.test/") };
    var service = new FinMindStockService(client, memory, config, NullLogger<FinMindStockService>.Instance);
    var result = await service.GetQuoteAsync("2330", default);
    var countBeforeCache = handler.Count;
    await service.GetQuoteAsync("2330", default);
    Check(handler.Count == countBeforeCache, "Result should be cached");
    Check(handler.Authorized, "Bearer token must be attached");
    return result;
}
var quote = (await Run(prices)).Quote!;
Check(quote.CurrentPrice == 110 && quote.ChangePercent == 10, "Use latest date and spread-derived previous close");
Check(quote.Name == "台積電" && quote.Volume == 123456 && quote.OpenPrice == 101, "Map name, shares and prices");
Check((await Run(prices.Replace("\"spread\":10", "\"spread\":110"))).Quote!.ChangePercent == null, "Avoid zero division");
Check((await Run("""{"status":200,"data":[]}""")).Quote!.TradeDate == null, "Empty prices must not become zero");
Check((await Run("""{"status":402,"msg":"quota"}""")).Quote == null, "Handle provider error inside HTTP 200");
Check((await Run("bad json")).Quote == null, "Handle malformed response");
Check((await Run("{}", HttpStatusCode.TooManyRequests)).Quote == null, "Handle HTTP quota");
Check(quote.Fundamentals.PE == 25 && quote.Fundamentals.RevenueYoY == 25 && quote.Fundamentals.RevenueMoM == 50, "Valuation and revenue period comparisons");
Check((await Run(prices, failFundamentals: true)).Quote!.CurrentPrice == 110, "Optional API failure must preserve quote");
var epoch = new DateOnly(2025, 1, 1);
var rising = Enumerable.Range(1, 80).Select(i => new DailyClose(epoch.AddDays(i), i)).ToArray();
var t = TechnicalIndicators.Calculate(rising.Reverse());
Check(t.MA5 == 78 && t.MA20 == 70.5m && t.MA60 == 50.5m && t.RSI14 == 100, "Rising series averages and RSI");
Check(Math.Abs(t.MACD!.Value - 7) < 0.000001m && Math.Abs(t.Signal!.Value - 7) < 0.000001m, "EMA seed and MACD linear ramp");
var flat = TechnicalIndicators.Calculate(rising.Select(x => x with { Close = 10m }));
Check(flat.RSI14 == 50 && flat.MACD == 0 && flat.Histogram == 0, "Flat series");
Check(TechnicalIndicators.Calculate(rising.Take(14)).RSI14 == null, "RSI warmup");
Check(TechnicalIndicators.Calculate(rising.Take(33)).Signal == null, "MACD signal warmup");
Check(TechnicalIndicators.Calculate(rising.Take(59)).MA60 == null, "MA60 warmup");
var broken = rising.ToArray(); broken[^3] = broken[^3] with { Close = null };
Check(TechnicalIndicators.Calculate(broken).MA5 == null, "Do not bridge missing closes");
var falling = TechnicalIndicators.Calculate(rising.Select(x => x with { Close = 100m - x.Close }));
Check(falling.RSI14 == 0, "Falling series RSI");
Console.WriteLine("PASS: fundamentals, partial failures, MA, Wilder RSI, MACD, warmup, sorting and missing data.");

Console.WriteLine("PASS: latest-day mapping, percent calculation, nulls, quota, malformed JSON, cache and token header.");

class FakeHandler(string info, string prices, HttpStatusCode priceStatus, bool failFundamentals) : HttpMessageHandler
{
    public int Count { get; private set; }
    public bool Authorized { get; private set; } = true;
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        Count++;
        Authorized &= request.Headers.Authorization?.ToString() == "Bearer test-token";
        if (request.RequestUri!.Query.Contains("TaiwanStockPER") || request.RequestUri.Query.Contains("TaiwanStockMonthRevenue"))
        {
            var today = DateTime.UtcNow;
            var month = new DateTime(today.Year, today.Month, 1).AddMonths(-1);
            var rows = new[] { (month, 150m), (month.AddMonths(-1), 100m), (month.AddYears(-1), 120m) }
                .Select(x => new { stock_id = "2330", date = today.ToString("yyyy-MM-dd"), revenue_year = x.Item1.Year, revenue_month = x.Item1.Month, revenue = x.Item2 });
            var json = request.RequestUri.Query.Contains("TaiwanStockPER")
                ? System.Text.Json.JsonSerializer.Serialize(new { status = 200, data = new[] { new { stock_id = "2330", date = today.ToString("yyyy-MM-dd"), PER = 25m, PBR = 3m, dividend_yield = 2m } } })
                : System.Text.Json.JsonSerializer.Serialize(new { status = 200, data = rows });
            return Task.FromResult(new HttpResponseMessage(failFundamentals ? HttpStatusCode.TooManyRequests : HttpStatusCode.OK) { Content = new StringContent(json) });
        }
        var isInfo = request.RequestUri!.Query.Contains("TaiwanStockInfo");
        return Task.FromResult(new HttpResponseMessage(isInfo ? HttpStatusCode.OK : priceStatus) {
            Content = new StringContent(isInfo ? info : prices, Encoding.UTF8, "application/json")
        });
    }
}
