using System.Net;
using System.Text;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using WorkJournal.Web.Services;

static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
var date = DateTime.UtcNow.ToString("yyyy-MM-dd");
var info = """{"status":200,"data":[{"stock_id":"2330","stock_name":"台積電","date":"2026-01-01","type":"twse"}]}""";
var prices = $$"""{"status":200,"data":[{"stock_id":"2330","date":"{{date}}","close":110,"spread":10,"open":101,"max":112,"min":100,"Trading_Volume":123456},{"stock_id":"2330","date":"2000-01-01","close":1,"spread":0}]}""";
async Task<StockLookup> Run(string price, HttpStatusCode status = HttpStatusCode.OK)
{
    using var memory = new MemoryCache(new MemoryCacheOptions());
    var handler = new FakeHandler(info, price, status);
    var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> { ["FinMind:Token"]="test-token" }).Build();
    using var client = new HttpClient(handler) { BaseAddress = new Uri("https://example.test/") };
    var service = new FinMindStockService(client, memory, config, NullLogger<FinMindStockService>.Instance);
    var result = await service.GetQuoteAsync("2330", default);
    await service.GetQuoteAsync("2330", default);
    Check(handler.Count == 2, "Result should be cached");
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
Console.WriteLine("PASS: latest-day mapping, percent calculation, nulls, quota, malformed JSON, cache and token header.");

class FakeHandler(string info, string prices, HttpStatusCode priceStatus) : HttpMessageHandler
{
    public int Count { get; private set; }
    public bool Authorized { get; private set; } = true;
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        Count++;
        Authorized &= request.Headers.Authorization?.ToString() == "Bearer test-token";
        var isInfo = request.RequestUri!.Query.Contains("TaiwanStockInfo");
        return Task.FromResult(new HttpResponseMessage(isInfo ? HttpStatusCode.OK : priceStatus) {
            Content = new StringContent(isInfo ? info : prices, Encoding.UTF8, "application/json")
        });
    }
}
