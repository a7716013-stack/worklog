using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace WorkJournal.MarketData;

public sealed class MarketDataGateway(HttpClient official, SourceCache cache, RequestBudget budget, TimeProvider clock)
{
    private DateOnly Today => DateOnly.FromDateTime(clock.GetUtcNow().ToOffset(TimeSpan.FromHours(8)).DateTime);
    private static bool Failure(Exception e, CancellationToken ct) => e is HttpRequestException or InvalidDataException or JsonException
        || e is OperationCanceledException && !ct.IsCancellationRequested;

    public async Task<JsonElement[]> ReadAsync(string dataset, string symbol, DateOnly? from, DateOnly? to,
        Func<Task<JsonElement[]>> finmind, CancellationToken ct)
    {
        Task<JsonElement[]>? backupTask = null;
        Task<JsonElement[]> Backup() => backupTask ??= BackupCore();
        async Task<JsonElement[]> BackupCore()
        {
            return await cache.GetAsync($"finmind:v1:{dataset}:{symbol}:{from}:{to}", TimeSpan.FromHours(6), async () =>
            {
                await budget.AcquireAsync("FinMind", ct);
                try
                {
                    var rows = await finmind();
                    return rows.Select(row =>
                    {
                        var fields = row.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone());
                        fields["_source"] = JsonSerializer.SerializeToElement("FinMind");
                        return JsonSerializer.SerializeToElement(fields);
                    }).ToArray();
                }
                catch (HttpRequestException ex) when (ex.StatusCode is HttpStatusCode.PaymentRequired or HttpStatusCode.TooManyRequests)
                {
                    await budget.BlockAsync("FinMind", TimeSpan.FromHours(1), CancellationToken.None);
                    throw new MarketDataException("FinMind 回報額度超限，已暫停新請求一小時；官方資料與已存快取仍可使用。");
                }
            }, ct);
        }
        try
        {
            if (dataset == "TaiwanStockInfo") return await CatalogAsync(ct);
            if (dataset is not ("TaiwanStockPrice" or "TaiwanStockInstitutionalInvestorsBuySell") || from == null || to == null)
                return await Backup();
            if (!Regex.IsMatch(symbol, @"^[0-9]{4}[0-9A-Z]{0,2}$") || from > to || to > Today)
                throw new ArgumentException("Invalid market data range or symbol.");
            var catalog = await CatalogAsync(ct);
            var stock = catalog.FirstOrDefault(x => OfficialParser.Text(x,"stock_id") == symbol);
            if (stock.ValueKind == JsonValueKind.Undefined) throw new InvalidDataException("官方清單查無此證券。");
            var market = OfficialParser.Text(stock,"type");
            if (dataset == "TaiwanStockInstitutionalInvestorsBuySell")
            {
                // Long backtests must use a complete archive, never silently truncate to recent flows.
                if (to.Value.DayNumber - from.Value.DayNumber > 90) return await Backup();
                var flows = new List<JsonElement>();
                for (var day = from.Value; day <= to; day = day.AddDays(1))
                {
                    if (day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) continue;
                    var date = day;
                    var url = market == "twse"
                        ? $"https://www.twse.com.tw/rwd/zh/fund/T86?date={date:yyyyMMdd}&selectType=ALLBUT0999&response=json"
                        : $"https://www.tpex.org.tw/www/zh-tw/insti/dailyTrade?type=Daily&sect=EW&date={date:yyyy/MM/dd}&id=&response=json";
                    var all = await JsonAsync($"flows:v1:{market}:{date}", url, date,
                        root => OfficialParser.Flows(root,market,date), ct);
                    flows.AddRange(all.Where(x => OfficialParser.Text(x,"stock_id") == symbol));
                }
                return flows.ToArray();
            }
            // A multi-year OTC backtest needs exact volume for every bar, not the price-only monthly report.
            if (market == "tpex" && to.Value.DayNumber - from.Value.DayNumber > 730) return await Backup();
            var prices = new List<JsonElement>();
            for (var month = new DateOnly(from.Value.Year,from.Value.Month,1); month <= to; month = month.AddMonths(1))
            {
                var date = month;
                var url = market == "twse"
                    ? $"https://www.twse.com.tw/rwd/zh/afterTrading/STOCK_DAY?date={date:yyyyMMdd}&stockNo={symbol}&response=json"
                    : $"https://www.tpex.org.tw/www/zh-tw/afterTrading/tradingStock?code={symbol}&date={date:yyyy/MM/dd}&response=json";
                var rows = await JsonAsync($"prices:v1:{market}:{symbol}:{date:yyyyMM}",url,date.AddMonths(1).AddDays(-1),
                    root => OfficialParser.MonthlyPrices(root,symbol,market,date),ct);
                prices.AddRange(rows.Where(x => DateOnly.TryParse(OfficialParser.Text(x,"date"),out var d) && d >= from && d <= to));
            }
            if (market == "tpex")
            {
                // Exact daily rows are cached for the entire OTC market and reused by other symbols.
                foreach (var row in prices.OrderByDescending(x => OfficialParser.Text(x,"date")).Take(21).ToArray())
                {
                    var date = DateOnly.Parse(OfficialParser.Text(row,"date"));
                    var daily = await JsonAsync($"prices-daily:v1:tpex:{date}",
                        $"https://www.tpex.org.tw/www/zh-tw/afterTrading/dailyQuotes?date={date:yyyy/MM/dd}&id=&response=json",
                        date,root => OfficialParser.DailyPrices(root,date),ct);
                    var exact = daily.FirstOrDefault(x => OfficialParser.Text(x,"stock_id") == symbol);
                    if (exact.ValueKind != JsonValueKind.Undefined)
                    {
                        var i = prices.FindIndex(x => OfficialParser.Text(x,"date") == date.ToString("yyyy-MM-dd"));
                        prices[i] = exact;
                    }
                }
            }
            return prices.OrderBy(x => OfficialParser.Text(x,"date")).ToArray();
        }
        catch (Exception e) when (Failure(e,ct))
        {
            try { return await Backup(); }
            catch (Exception backupError) when (Failure(backupError,ct))
            {
                throw new MarketDataException($"資料來源暫不可用。{(backupError is MarketDataException ? backupError.Message : "備援來源連線、授權或格式異常。")}");
            }
        }
    }

    public async Task<JsonElement[]> CatalogAsync(CancellationToken ct)
    {
        var result = new List<JsonElement>();
        foreach (var (market,mode) in new[] { ("twse",2),("tpex",4) })
        {
            var rows = await cache.GetAsync("catalog:v1:" + market,TimeSpan.FromDays(1),async () =>
            {
                await budget.AcquireAsync("Official",ct);
                using var response = await official.GetAsync($"https://isin.twse.com.tw/isin/C_public.jsp?strMode={mode}",ct);
                await CheckResponse(response,ct);
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
                var html = Encoding.GetEncoding(950).GetString(await response.Content.ReadAsByteArrayAsync(ct));
                return OfficialParser.Catalog(html,market);
            },ct);
            result.AddRange(rows);
        }
        return result.ToArray();
    }
    private Task<JsonElement[]> JsonAsync(string key,string url,DateOnly date,Func<JsonElement,JsonElement[]> parse,CancellationToken ct) =>
        cache.GetAsync(key,date >= Today.AddDays(-7) ? TimeSpan.FromMinutes(30) : TimeSpan.FromDays(30),async () =>
        {
            await budget.AcquireAsync("Official",ct);
            using var response = await official.GetAsync(url,ct); await CheckResponse(response,ct);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            return parse(json.RootElement);
        },ct);
    private async Task CheckResponse(HttpResponseMessage response,CancellationToken ct)
    {
        if (response.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.Forbidden)
        {
            await budget.BlockAsync("Official",TimeSpan.FromMinutes(15),CancellationToken.None);
            throw new MarketDataException("官方來源暫時拒絕或限制請求，已啟動 15 分鐘冷卻。");
        }
        response.EnsureSuccessStatusCode();
    }
}
