using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
public sealed class FixtureMarket : HttpMessageHandler
{
    public decimal Price { get; set; } = 100m;
    public decimal Low { get; set; } = 95m;
    public decimal High { get; set; } = 105m;
    public bool Failure { get; set; }
    public DateOnly Date { get; set; } = DateOnly.FromDateTime(DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(8)).DateTime).AddDays(-1);
    public int Requests { get; private set; }
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Requests++;
        if (Failure) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        var query = QueryHelpers.ParseQuery(request.RequestUri!.Query);
        var symbol = query["data_id"].ToString();
        var dataset = query["dataset"].ToString();
        object[] data;
        if (dataset == "TaiwanStockInfo")
            data = new[] { ("2330", "台積電", "半導體業"), ("006208", "富邦台50", "ETF"), ("0050", "元大台灣50", "ETF") }
                .Where(x => symbol == "" || x.Item1 == symbol)
                .Select(x => (object)new { stock_id = x.Item1, stock_name = x.Item2, industry_category = x.Item3, type = "twse", date = Date.ToString("yyyy-MM-dd") }).ToArray();
        else if (dataset == "TaiwanStockPrice")
        {
            var from = DateOnly.TryParse(query["start_date"], out var start) ? start : Date.AddDays(-400);
            data = Enumerable.Range(0, Math.Max(0, Date.DayNumber - from.DayNumber + 1)).Select(i => from.AddDays(i))
                .Where(d => d.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday || d == Date)
                .Select(d => (object)new { stock_id = symbol, date = d.ToString("yyyy-MM-dd"), open = Price, max = High, min = Low, close = Price, spread = 0, Trading_Volume = 1000000 }).ToArray();
        }
        else if (dataset == "TaiwanStockInstitutionalInvestorsBuySell")
        {
            var from = DateOnly.TryParse(query["start_date"], out var start) ? start : Date.AddDays(-400);
            data = Enumerable.Range(0, Math.Max(0, Date.DayNumber - from.DayNumber + 1)).SelectMany(i => new[] { "Foreign_Investor", "Investment_Trust" }
                .Select(name => (object)new { stock_id = symbol, date = from.AddDays(i).ToString("yyyy-MM-dd"), name, buy = 1000, sell = 100 })).ToArray();
        }
        else data = [];
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { status = 200, data })) });
    }
}
