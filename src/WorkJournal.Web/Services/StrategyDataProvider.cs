using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using WorkJournal.Web.Data;
using WorkJournal.Web.Models;
namespace WorkJournal.Web.Services;

public interface IStrategyDataProvider
{
    int ApiCalls { get; }
    int CacheHits { get; }
    Task<StrategySecurity[]> SecuritiesAsync(CancellationToken ct);
    Task<DateOnly[]> CalendarAsync(CancellationToken ct);
    Task<StrategyInput> LoadAsync(StrategySecurity stock, DateOnly date, DateOnly[] sessions, CancellationToken ct);
    Task<StrategyPrice[]> PricesAsync(string symbol, DateOnly date, CancellationToken ct);
    Task CaptureEventsAsync(CancellationToken ct) => Task.CompletedTask;
}
public class StrategyRateLimitException : Exception { public StrategyRateLimitException() : base("資料準備已達本小時請求預算，下一輪續跑；股票池未縮減。") { } }
public class StrategyDataProvider(JournalDbContext db, FinMindStockService finmind, IHttpClientFactory clients,
    ITaiwanMarketRankingProvider market, TimeProvider clock, IOptions<StrategyScanOptions> options) : IStrategyDataProvider
{
    private static readonly Queue<DateTimeOffset> Requests = new();
    private static readonly SemaphoreSlim RequestGate = new(1, 1);
    public int ApiCalls { get; private set; }
    public int CacheHits { get; private set; }
    public async Task CaptureEventsAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow().ToOffset(TimeSpan.FromHours(8));
        if (now.Hour < 23) return;
        var date = DateOnly.FromDateTime(now.DateTime);
        var snapshot = await market.GetAsync(ct);
        if (snapshot.Warnings.Any(x => x.Contains("重大資訊"))) return;
        foreach (var exchange in new[] { "twse", "tpex" })
        {
            var key = $"events:{exchange}:{date:yyyyMMdd}";
            if (await db.StrategySourceCaches.AnyAsync(x => x.Key == key, ct)) continue;
            db.Add(new StrategySourceCache { Key = key, Json = JsonSerializer.Serialize(snapshot.Events.Where(x => x.PublishedAt <= clock.GetUtcNow()).ToArray()), FetchedAt = clock.GetUtcNow(), ExpiresAt = clock.GetUtcNow().AddDays(30) });
        }
        await db.SaveChangesAsync(ct);
    }
    private async Task<string> Cached(string key, TimeSpan ttl, Func<Task<string>> fetch, CancellationToken ct)
    {
        var row = await db.StrategySourceCaches.SingleOrDefaultAsync(x => x.Key == key, ct);
        if (row != null && row.ExpiresAt > clock.GetUtcNow()) { CacheHits++; return row.Json; }
        await RequestGate.WaitAsync(ct);
        string json;
        try
        {
            while (Requests.TryPeek(out var date) && date < clock.GetUtcNow().AddHours(-1)) Requests.Dequeue();
            if (Requests.Count >= options.Value.RequestsPerHour) throw new StrategyRateLimitException();
            Requests.Enqueue(clock.GetUtcNow()); ApiCalls++;
            json = await fetch();
            await Task.Delay(250, ct);
        }
        finally { RequestGate.Release(); }
        if (row == null) { row = new() { Key = key }; db.Add(row); }
        row.Json = json; row.FetchedAt = clock.GetUtcNow(); row.ExpiresAt = row.FetchedAt.Add(ttl);
        await db.SaveChangesAsync(ct); return json;
    }
    private Task<string> Http(string key, string url, CancellationToken ct, bool big5 = false) => Cached(key, TimeSpan.FromHours(4), async () =>
    {
        var client = clients.CreateClient("StrategyOfficial");
        using var response = await client.GetAsync(url, ct); response.EnsureSuccessStatusCode();
        var bytes = await response.Content.ReadAsByteArrayAsync(ct);
        if (big5) { Encoding.RegisterProvider(CodePagesEncodingProvider.Instance); return Encoding.GetEncoding(950).GetString(bytes); }
        return Encoding.UTF8.GetString(bytes);
    }, ct);
    private async Task<JsonElement[]> Dataset(string name, string symbol, DateOnly? from, DateOnly? date, TimeSpan ttl, CancellationToken ct)
    {
        var json = await Cached($"finmind:{name}:{symbol}:{date:yyyyMMdd}", ttl,
            async () => JsonSerializer.Serialize(await finmind.StrategyDatasetAsync(name, symbol, from, date, ct)), ct);
        return JsonSerializer.Deserialize<JsonElement[]>(json)!;
    }
    public async Task<DateOnly[]> CalendarAsync(CancellationToken ct)
    {
        var rows = await Dataset("TaiwanStockTradingDate", "", null, null, TimeSpan.FromHours(4), ct);
        var dates = rows.Select(x => DateOnly.TryParse(Text(x, "date"), out var d) ? (DateOnly?)d : null).Where(x => x.HasValue).Select(x => x!.Value).Distinct().Order().ToArray();
        if (dates.Length < 120) throw new InvalidDataException("交易日曆來源不足。");
        return dates;
    }
    public static StrategySecurity[] ParseIsin(string html, string market)
    {
        var result = new List<StrategySecurity>();
        foreach (Match row in Regex.Matches(html, @"<tr\b[^>]*>(.*?)</tr>", RegexOptions.Singleline | RegexOptions.IgnoreCase))
        {
            var cells = Regex.Matches(row.Value, @"<td\b[^>]*>(.*?)</td>", RegexOptions.Singleline | RegexOptions.IgnoreCase)
                .Select(x => WebUtility.HtmlDecode(Regex.Replace(x.Groups[1].Value, "<[^>]+>", "")).Trim()).ToArray();
            if (cells.Length < 7 || cells[5].Length != 6 || cells[1].Length != 12) continue;
            var pair = Regex.Split(cells[0], @"\s+", RegexOptions.None);
            if (pair.Length < 2) continue;
            result.Add(new(pair[0], string.Join(" ", pair.Skip(1)), market, cells[4], cells[5], string.IsNullOrWhiteSpace(cells[6])));
        }
        if (!result.Any(x => x.Cfi.StartsWith("ES"))) throw new InvalidDataException("官方 CFI 證券分類格式異常。");
        return result.ToArray();
    }
    public async Task<StrategySecurity[]> SecuritiesAsync(CancellationToken ct)
    {
        var a = ParseIsin(await Http("isin:twse", "https://isin.twse.com.tw/isin/C_public.jsp?strMode=2", ct, true), "twse");
        var b = ParseIsin(await Http("isin:tpex", "https://isin.twse.com.tw/isin/C_public.jsp?strMode=4", ct, true), "tpex");
        // Current official abnormal-trading lists; fail closed if classification/status cannot be confirmed.
        var excluded = new HashSet<string>();
        foreach (var source in new[] {
            ("status:twse:altered", "https://openapi.twse.com.tw/v1/exchangeReport/TWT85U"),
            ("status:twse:suspended", "https://openapi.twse.com.tw/v1/exchangeReport/TWTAWU"),
            ("status:tpex", "https://www.tpex.org.tw/openapi/v1/tpex_cmode"),
            ("disposal:twse", "https://openapi.twse.com.tw/v1/announcement/punish"),
            ("disposal:tpex", "https://www.tpex.org.tw/openapi/v1/tpex_disposal_information") })
        {
            using var doc = JsonDocument.Parse(await Http(source.Item1, source.Item2, ct));
            if (doc.RootElement.ValueKind != JsonValueKind.Array) throw new InvalidDataException("官方交易狀態來源格式異常。");
            foreach (var row in doc.RootElement.EnumerateArray().Where(x => StatusActive(x, DateOnly.FromDateTime(clock.GetUtcNow().ToOffset(TimeSpan.FromHours(8)).DateTime))))
                foreach (var prop in row.EnumerateObject())
                    if (prop.Name is "Code" or "SecuritiesCode" or "SecuritiesCompanyCode" or "股票代號" or "證券代號" or "公司代號") excluded.Add(prop.Value.ToString().Trim());
        }
        return a.Concat(b).DistinctBy(x => x.Symbol).Select(x => x with { NormalTrading = x.NormalTrading && !excluded.Contains(x.Symbol) }).ToArray();
    }
    public static bool StatusActive(JsonElement row, DateOnly day)
    {
        if (row.TryGetProperty("TradingHaltDate", out var halt))
        {
            var begin = TaiwanMarketRankingProvider.Date(halt.ToString());
            var end = TaiwanMarketRankingProvider.Date(Text(row, "TradingResumptionDate"));
            return begin.HasValue && begin <= day && (!end.HasValue || day < end);
        }
        if (row.TryGetProperty("DispositionPeriod", out var period))
        {
            var dates = Regex.Split(period.ToString(), "[~～至]").Select(TaiwanMarketRankingProvider.Date).ToArray();
            if (dates.Length != 2 || dates.Any(x => !x.HasValue)) throw new InvalidDataException("無法判讀處置期間，暫停資格判定。");
            return day >= dates[0] && day <= dates[1];
        }
        return true;
    }
    private static string Text(JsonElement x, string key) => x.TryGetProperty(key, out var v) ? v.ToString() : "";
    private static decimal? Number(JsonElement x, string key) => decimal.TryParse(Text(x, key), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var n) ? n : null;
    public async Task<StrategyPrice[]> PricesAsync(string symbol, DateOnly date, CancellationToken ct)
    {
        var key = $"prices:{symbol}:{date:yyyyMMdd}";
        var current = await db.StrategySourceCaches.AsNoTracking().SingleOrDefaultAsync(x => x.Key == key && x.ExpiresAt > clock.GetUtcNow(), ct);
        if (current != null) { CacheHits++; return JsonSerializer.Deserialize<StrategyPrice[]>(current.Json)!; }
        var prior = await db.StrategySourceCaches.AsNoTracking().Where(x => x.Key.StartsWith($"prices:{symbol}:")).OrderByDescending(x => x.FetchedAt).FirstOrDefaultAsync(ct);
        var old = prior == null ? [] : JsonSerializer.Deserialize<StrategyPrice[]>(prior.Json)!.Where(x => x.Date <= date).ToArray();
        var start = old.Length > 0 ? old.Max(x => x.Date).AddDays(-5) : date.AddDays(-500);
        var rows = await Dataset("TaiwanStockPrice", symbol, start, date, TimeSpan.FromHours(4), ct);
        var incoming = rows.Where(x => Text(x, "stock_id") == symbol && DateOnly.TryParse(Text(x, "date"), out var d) && d <= date)
            .Select(x => new StrategyPrice(DateOnly.Parse(Text(x, "date")), Number(x, "open"), Number(x, "max"), Number(x, "min"), Number(x, "close"), Number(x, "Trading_Volume"), Number(x, "Trading_money")))
            .ToArray();
        var merged = old.Concat(incoming).GroupBy(x => x.Date).Select(g => g.Last()).OrderBy(x => x.Date).TakeLast(500).ToArray();
        var saved = await db.StrategySourceCaches.SingleOrDefaultAsync(x => x.Key == key, ct);
        if (saved == null) { saved = new() { Key = key }; db.Add(saved); }
        saved.Json = JsonSerializer.Serialize(merged); saved.FetchedAt = clock.GetUtcNow(); saved.ExpiresAt = saved.FetchedAt.AddHours(4);
        await db.SaveChangesAsync(ct); return merged;
    }
    public async Task<StrategyInput> LoadAsync(StrategySecurity stock, DateOnly date, DateOnly[] sessions, CancellationToken ct)
    {
        var prices = await PricesAsync(stock.Symbol, date, ct);
        SwingFlow[] flows = []; decimal? revenue = null; DateOnly? month = null;
        try { flows = FinMindStockService.ParseSwingFlows(await Dataset("TaiwanStockInstitutionalInvestorsBuySell", stock.Symbol, date.AddDays(-65), date, TimeSpan.FromHours(4), ct), stock.Symbol); }
        catch (Exception e) when (TaiwanMarketRankingProvider.Failure(e, ct)) { }
        try
        {
            var rows = await Dataset("TaiwanStockMonthRevenue", stock.Symbol, date.AddMonths(-16), date, TimeSpan.FromHours(12), ct);
            var periods = rows.Where(x => Text(x, "stock_id") == stock.Symbol && DateOnly.TryParse(Text(x, "date"), out var published) && published <= date)
                .Where(x => Number(x, "revenue_year") > 1900 && Number(x, "revenue_month") is >= 1 and <= 12)
                .GroupBy(x => new DateOnly((int)Number(x, "revenue_year")!.Value, (int)Number(x, "revenue_month")!.Value, 1))
                .ToDictionary(x => x.Key, x => x.OrderBy(r => Text(r, "date")).Last());
            if (periods.Count > 0)
            {
                month = periods.Keys.Max(); var current = new DateOnly(date.Year, date.Month, 1);
                if (month < current && month >= current.AddMonths(-2) && periods.TryGetValue(month.Value.AddYears(-1), out var previous) && Number(previous, "revenue") > 0)
                    revenue = (Number(periods[month.Value], "revenue") / Number(previous, "revenue") - 1) * 100;
            }
        }
        catch (Exception e) when (TaiwanMarketRankingProvider.Failure(e, ct)) { }
        var snapshot = await market.GetAsync(ct);
        // The current-day event endpoint does not prove seven-day coverage; never silently call an empty result complete.
        var events = snapshot.Events.Where(x => x.PublishedAt <= clock.GetUtcNow() && DateOnly.FromDateTime(x.PublishedAt.ToOffset(TimeSpan.FromHours(8)).DateTime) <= date).ToArray();
        var localNow = clock.GetUtcNow().ToOffset(TimeSpan.FromHours(8));
        var today = DateOnly.FromDateTime(localNow.DateTime);
        if (localNow.Hour >= 23 && !snapshot.Warnings.Any(x => x.Contains("重大資訊")))
        {
            var key = $"events:{stock.Market}:{today:yyyyMMdd}";
            if (!await db.StrategySourceCaches.AnyAsync(x => x.Key == key, ct))
            { db.Add(new StrategySourceCache { Key = key, Json = JsonSerializer.Serialize(events), FetchedAt = clock.GetUtcNow(), ExpiresAt = clock.GetUtcNow().AddDays(30) }); await db.SaveChangesAsync(ct); }
        }
        var eventKeys = Enumerable.Range(0, 8).Select(i => $"events:{stock.Market}:{date.AddDays(-i):yyyyMMdd}").ToArray();
        var savedEvents = await db.StrategySourceCaches.AsNoTracking().Where(x => eventKeys.Contains(x.Key)).ToListAsync(ct);
        events = events.Concat(savedEvents.SelectMany(x => JsonSerializer.Deserialize<MarketEvent[]>(x.Json)!)).DistinctBy(x => (x.StockId,x.Title,x.PublishedAt)).ToArray();
        var heatRows = await db.MarketPulseDays.AsNoTracking().Where(x => x.Market == stock.Market && x.TradeDate <= date).OrderByDescending(x => x.TradeDate).Take(21).ToListAsync(ct);
        var pulse = heatRows.Select(x => JsonSerializer.Deserialize<PulseMarket>(x.Json)!).ToArray();
        decimal? heat = pulse.FirstOrDefault()?.Date == date ? MarketHeatCalculator.Calculate(pulse[0], pulse.Skip(1).ToArray()).Score : null;
        return new(stock, date, prices, flows, sessions, revenue, month, events, savedEvents.Count == 8, heat, clock.GetUtcNow());
    }
}
