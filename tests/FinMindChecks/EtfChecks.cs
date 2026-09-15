using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using WorkJournal.Web.Models;
using WorkJournal.Web.Services;
static class EtfChecks
{
    static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
    public static async Task Run()
    {
        Check(FinMindStockService.IsEtfCategory("ETF") && FinMindStockService.IsEtfCategory("主動式ETF") && !FinMindStockService.IsEtfCategory("半導體業") && !FinMindStockService.IsEtfCategory(null), "ETF classification must use metadata");
        const string html = """
        資料日期：2026/09/14
        <p>基金淨資產(新台幣)</p><p>454,743,700,318</p>
        <table><tr><td>期貨代碼</td></tr><tr><td>WTXV6F</td><td>期貨</td><td>180</td><td>1000</td><td>0.36</td></tr></table>
        <table><tr><td>股票代碼</td></tr>
        <tr><td>2317</td><td>鴻海</td><td>5</td><td>100</td><td>4.2</td></tr>
        <tr><td>2330</td><td>台積電</td><td>10</td><td>200</td><td>56.69</td></tr>
        <tr><td>股票合計</td><td></td><td></td><td></td><td>99</td></tr></table>
        """;
        var parsed = new EtfData();
        EtfOfficialService.ParseAssets(html, parsed);
        Check(parsed.Assets == 454743700318m && parsed.HoldingsDate == new DateOnly(2026,9,14), "Official assets units and date");
        Check(parsed.Holdings.Count == 2 && parsed.Holdings[0].Symbol == "2330", "Holdings sort and exclusion of futures/total");
        var noDate = new EtfData();
        EtfOfficialService.ParseAssets(html.Replace("資料日期：2026/09/14",""), noDate);
        Check(noDate.Assets == null && noDate.Holdings.Count == 0, "Reject undated holdings");
        using var dividends = JsonDocument.Parse("""
        [{"stock_id":"006208","date":"2026-07-18","CashExDividendTradingDate":"2026-07-16","CashEarningsDistribution":0,"CashStatutorySurplus":0,"StockEarningsDistribution":0,"StockStatutorySurplus":0},
         {"stock_id":"006208","date":"2026-07-22","CashExDividendTradingDate":"2026-07-16","CashEarningsDistribution":4.75,"CashStatutorySurplus":0,"StockEarningsDistribution":0,"StockStatutorySurplus":0,"CashDividendPaymentDate":"2026-08-10"},
         {"stock_id":"2330","date":"2026-07-22","CashExDividendTradingDate":"2026-07-16","CashEarningsDistribution":99}]
        """);
        var events = FinMindStockService.ParseDistributions(dividends.RootElement.EnumerateArray(), "006208");
        Check(events.Count == 1 && events[0].Cash == 4.75m && events[0].Stock == 0, "Latest dividend revision replaces duplicate and filters symbol");
        const string nav = """{"netPrice":[{"date":"2026/09/14","count":244.41}],"atmps":[{"date":"2026/09/13","count":5},{"date":"2026/09/14","count":0.10}]}""";
        using var client = new HttpClient(new OfficialHandler(nav, html));
        var service = new EtfOfficialService(client, NullLogger<EtfOfficialService>.Instance);
        var result = await service.GetAsync("006208", "富邦台50", new DateOnly(2026,9,15), default);
        Check(result.Nav == 244.41m && result.Premium == 0.10m && result.Holdings.Count == 2, "Official NAV and premium must share date");
        using var badClient = new HttpClient(new OfficialHandler("""{"netPrice":[{"date":"2026/09/14","count":"bad"}]}""", html));
        result = await new EtfOfficialService(badClient, NullLogger<EtfOfficialService>.Instance).GetAsync("006208","富邦台50",new DateOnly(2026,9,15),default);
        Check(result.Nav == null && result.Holdings.Count == 2, "Malformed NAV must preserve holdings");
        Console.WriteLine("PASS: ETF classification, dated NAV/premium, assets units, holdings, dividend revisions and partial failure.");
    }
    sealed class OfficialHandler(string nav, string html) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Check(request.Headers.Authorization == null, "Never forward FinMind token to official sources");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content=new StringContent(request.Method == HttpMethod.Post ? nav : html) });
        }
    }
}
