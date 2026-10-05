using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Caching.Memory;
using WorkJournal.Web.Models;

namespace WorkJournal.Web.Services;

public interface ITaiwanMarketRankingProvider
{
    Task<MarketSnapshot> GetAsync(CancellationToken ct);
}

public class TaiwanMarketRankingProvider(HttpClient client, FinMindStockService stocks, IMemoryCache cache,
    ILogger<TaiwanMarketRankingProvider> logger) : ITaiwanMarketRankingProvider
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    public async Task<MarketSnapshot> GetAsync(CancellationToken ct)
    {
        var key = "radar:official:snapshot:" + DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(8)).ToString("yyyyMMdd");
        if (cache.TryGetValue<MarketSnapshot>(key, out var hit)) return hit!;
        await Gate.WaitAsync(ct);
        try
        {
            if (cache.TryGetValue(key, out hit)) return hit!;
            var warnings = new List<string>();
            async Task<JsonElement?> Fetch(string url, string label)
            {
                try { using var response = await client.GetAsync(url, ct); response.EnsureSuccessStatusCode();
                    using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct)); return doc.RootElement.Clone(); }
                catch (Exception e) when (Failure(e, ct))
                { logger.LogWarning("Market source {Source} failed ({Type})", label, e.GetType().Name); lock(warnings) warnings.Add(label + "暫時無法取得。"); return null; }
            }
            var twse = Fetch("https://www.twse.com.tw/exchangeReport/MI_INDEX?response=json&type=ALLBUT0999", "上市行情");
            var tpex = Fetch("https://www.tpex.org.tw/openapi/v1/tpex_mainboard_daily_close_quotes", "上櫃行情");
            var ev1 = Fetch("https://openapi.twse.com.tw/v1/opendata/t187ap04_L", "上市重大資訊");
            var ev2 = Fetch("https://www.tpex.org.tw/openapi/v1/mopsfin_t187ap04_O", "上櫃重大資訊");
            SwingStock[] catalog;
            try { catalog = await stocks.SwingCatalogAsync(ct); }
            catch (Exception e) when (Failure(e, ct)) { catalog = []; warnings.Add("商品分類暫時無法取得，無法確認股票及 ETF，請稍後重試。"); }
            await Task.WhenAll(twse,tpex,ev1,ev2);
            var quotes = new List<MarketQuote>();
            if (await twse is { } listed) quotes.AddRange(ParseTwse(listed,catalog));
            if (!quotes.Any(x => x.Stock.Market == "twse"))
            {
                var fallback = await Fetch("https://openapi.twse.com.tw/v1/exchangeReport/STOCK_DAY_ALL", "上市備援行情");
                if (fallback is { } raw) quotes.AddRange(ParseArray(raw,catalog,"twse"));
            }
            if (await tpex is { } otc) quotes.AddRange(ParseArray(otc,catalog,"tpex"));
            foreach (var market in new[] {"twse","tpex"}) if (!quotes.Any(x => x.Stock.Market == market)) warnings.Add((market=="twse"?"上市":"上櫃")+"目前沒有可用行情，排行範圍不完整。");
            var events = new List<MarketEvent>();
            if (await ev1 is { } a) events.AddRange(ParseEvents(a,"TWSE"));
            if (await ev2 is { } b) events.AddRange(ParseEvents(b,"TPEx"));
            var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(8)).DateTime);
            var snapshot = new MarketSnapshot(quotes.Where(x => x.Date <= today).DistinctBy(x => (x.Stock.Symbol,x.Date)).ToArray(),
                events.DistinctBy(x => (x.StockId,x.Title,x.PublishedAt)).OrderByDescending(x => x.PublishedAt).ToArray(), warnings.Distinct().ToArray(), DateTimeOffset.UtcNow);
            cache.Set(key,snapshot,TimeSpan.FromMinutes(warnings.Count > 0 ? 1 : 5));
            return snapshot;
        }
        finally { Gate.Release(); }
    }

    public static bool Failure(Exception e, CancellationToken ct) => e is HttpRequestException or JsonException or InvalidDataException or FormatException
        || e is OperationCanceledException && !ct.IsCancellationRequested;
    public static decimal? Number(string? s) => decimal.TryParse(s?.Replace(",", "").Trim(), NumberStyles.Number, CultureInfo.InvariantCulture,out var n) ? n : null;
    public static DateOnly? Date(string? value)
    {
        var digits = Regex.Replace(value ?? "", "[^0-9]", "");
        if (digits.Length == 7 && int.TryParse(digits[..3],out var roc)) digits=(roc+1911).ToString()+digits[3..];
        return DateOnly.TryParseExact(digits,"yyyyMMdd",CultureInfo.InvariantCulture,DateTimeStyles.None,out var date) ? date : null;
    }
    private static string Text(JsonElement row, string field) => row.TryGetProperty(field,out var v) ? v.ToString().Trim() : "";
    public static MarketQuote[] ParseArray(JsonElement root, SwingStock[] catalog, string market)
    {
        if (root.ValueKind != JsonValueKind.Array) return [];
        var lookup = catalog.Where(x => x.Market==market).ToDictionary(x=>x.Symbol);
        var result = new List<MarketQuote>();
        foreach(var row in root.EnumerateArray())
        {
            if(row.ValueKind!=JsonValueKind.Object) continue;
            var symbol=Text(row,market=="twse"?"Code":"SecuritiesCompanyCode");
            if (!lookup.TryGetValue(symbol,out var stock)) continue;
            var date=Date(Text(row,"Date")); var close=Number(Text(row,market=="twse"?"ClosingPrice":"Close"));
            var volume=Number(Text(row,market=="twse"?"TradeVolume":"TradingShares"));
            if(date.HasValue && close > 0 && volume >= 0) result.Add(new(stock,date.Value,close.Value,Number(Text(row,"Change")),volume.Value));
        }
        return result.ToArray();
    }
    public static MarketQuote[] ParseTwse(JsonElement root, SwingStock[] catalog)
    {
        if(root.ValueKind!=JsonValueKind.Object || !root.TryGetProperty("tables",out var tables) || tables.ValueKind!=JsonValueKind.Array || Date(Text(root,"date")) is not { } date) return [];
        var lookup=catalog.Where(x=>x.Market=="twse").ToDictionary(x=>x.Symbol);
        var result=new List<MarketQuote>();
        foreach(var table in tables.EnumerateArray())
        {
            if(table.ValueKind!=JsonValueKind.Object || !table.TryGetProperty("fields",out var fields) || fields.ValueKind!=JsonValueKind.Array || !table.TryGetProperty("data",out var data) || data.ValueKind!=JsonValueKind.Array) continue;
            var names=fields.EnumerateArray().Select(x=>x.ToString()).ToArray();
            int Field(string name)=>Array.IndexOf(names,name);
            if(new[]{"證券代號","收盤價","成交股數","漲跌價差","漲跌(+/-)"}.Any(x=>Field(x)<0)) continue;
            foreach(var row in data.EnumerateArray())
            {
                if(row.ValueKind!=JsonValueKind.Array) continue;
                var values=row.EnumerateArray().Select(x=>x.ToString()).ToArray();
                if(values.Length<names.Length || !lookup.TryGetValue(values[Field("證券代號")],out var stock)) continue;
                var close=Number(values[Field("收盤價")]); var volume=Number(values[Field("成交股數")]);
                var sign=Regex.Replace(values[Field("漲跌(+/-)")],"<[^>]*>","").Trim();
                var change=Number(values[Field("漲跌價差")]);
                change=sign=="-"?-change:sign is "+" or ""?change:null;
                if(close>0 && volume>=0) result.Add(new(stock,date,close.Value,change,volume.Value));
            }
        }
        return result.ToArray();
    }
    public static MarketEvent[] ParseEvents(JsonElement root,string source)
    {
        if(root.ValueKind!=JsonValueKind.Array) return [];
        var result=new List<MarketEvent>();
        foreach(var row in root.EnumerateArray())
        {
            if(row.ValueKind!=JsonValueKind.Object) continue;
            var symbol=Text(row,source=="TWSE"?"公司代號":"SecuritiesCompanyCode");
            var title=row.EnumerateObject().FirstOrDefault(x=>x.Name.Trim()=="主旨").Value;
            if(title.ValueKind!=JsonValueKind.String || Date(Text(row,"發言日期")) is not { } date) continue;
            var time=Text(row,"發言時間").PadLeft(6,'0');
            if(!TimeOnly.TryParseExact(time,"HHmmss",CultureInfo.InvariantCulture,DateTimeStyles.None,out var clock)) clock=TimeOnly.MinValue;
            var heading=title.GetString()??"";
            var category=heading.Contains("營收")?"營收":heading.Contains("法人說明")||heading.Contains("法說")?"法說會":heading.Contains("股利")?"股利":heading.Contains("增資")?"增資":"重大訊息";
            var summary=Text(row,"說明"); if(summary.Length>600) summary=summary[..600]+"…";
            result.Add(new(symbol,heading,category,new DateTimeOffset(date.ToDateTime(clock),TimeSpan.FromHours(8)),source,
                source=="TWSE"?"https://openapi.twse.com.tw/v1/opendata/t187ap04_L":"https://www.tpex.org.tw/openapi/v1/mopsfin_t187ap04_O","Unknown",summary));
        }
        return result.ToArray();
    }
}
