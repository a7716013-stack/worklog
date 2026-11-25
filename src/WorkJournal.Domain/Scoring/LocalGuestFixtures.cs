using System.Text.Json;
using WorkJournal.Web.Models;
namespace WorkJournal.Domain.Scoring;
// Deterministic synthetic input. Not historical observations, approvals, or research performance.
public static class LocalGuestFixtures
{
    public static readonly string[] Symbols = ["2330", "2317", "2454", "2308", "2881", "2882", "2603", "3008", "6488", "5347"];
    public static SwingStock[] Stocks => Symbols.Select((s,i) => new SwingStock(s, "訪客測試 " + s, i < 8 ? "twse" : "tpex", false)).ToArray();
    public static JsonElement[] Read(string dataset, string symbol, DateOnly? from, DateOnly? to)
    {
        if (dataset == "TaiwanStockInfo") return Stocks.Select(s => JsonSerializer.SerializeToElement(new { stock_id=s.Symbol, stock_name=s.Name, type=s.Market, industry_category="測試資料", date="2020-01-01" })).ToArray();
        if (!Symbols.Contains(symbol)) return [];
        var end = to ?? DateOnly.FromDateTime(DateTime.UtcNow); var start = from ?? end.AddDays(-420);
        if (end.DayNumber - start.DayNumber > 2500) start = end.AddDays(-2500);
        var dates = Enumerable.Range(0, Math.Max(0,end.DayNumber-start.DayNumber+1)).Select(i => start.AddDays(i)).Where(d => d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday));
        decimal Close(DateOnly d) => 100m + (d.DayNumber % 1000)*.02m + (d.DayNumber%7)*.1m;
        if (dataset == "TaiwanStockPrice") return dates.Select(d => JsonSerializer.SerializeToElement(new { date=d.ToString("yyyy-MM-dd"), stock_id=symbol, open=Close(d)-.1m, max=Close(d)+1, min=Close(d)-1, close=Close(d), spread=.2m, Trading_Volume=1000000L+(d.DayNumber%10)*10000, _source="SYNTHETIC_FIXTURE" })).ToArray();
        if (dataset == "TaiwanStockInstitutionalInvestorsBuySell") return dates.SelectMany(d => new[]{"Foreign_Investor","Investment_Trust","Dealer"}.Select(n => JsonSerializer.SerializeToElement(new {date=d.ToString("yyyy-MM-dd"),stock_id=symbol,name=n,buy=10000,sell=2000,_source="SYNTHETIC_FIXTURE"}))).ToArray();
        return []; // Unknown facts are absent, never invented as real data.
    }
}
