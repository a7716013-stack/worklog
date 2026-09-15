using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using WorkJournal.Web.Models;
namespace WorkJournal.Web.Services;

public class EtfOfficialService(HttpClient client, ILogger<EtfOfficialService> logger)
{
    static MatchCollection Matches(string html, string pattern) => Regex.Matches(html, pattern, RegexOptions.Singleline, TimeSpan.FromSeconds(2));
    static string Clean(string html) => WebUtility.HtmlDecode(Regex.Replace(html, "<[^>]+>", "", RegexOptions.Singleline, TimeSpan.FromSeconds(2))).Trim();
    static decimal? Decimal(string s) => decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out var v) ? v : null;
    public static void ParseAssets(string html, EtfData result)
    {
        var date = Matches(html, @"資料日期[：:]\s*(\d{4}/\d{2}/\d{2})").Cast<Match>().FirstOrDefault();
        if (date == null || !DateOnly.TryParseExact(date.Groups[1].Value, "yyyy/MM/dd", out var d)) return;
        result.AssetsDate = d;
        result.HoldingsDate = d;
        var assets = Matches(html, @"<p>基金淨資產\(新台幣\)</p>\s*<p>([^<]+)</p>").Cast<Match>().FirstOrDefault();
        result.Assets = assets == null ? null : Decimal(Clean(assets.Groups[1].Value));
        foreach (Match table in Matches(html, @"<table\b[^>]*>(.*?)</table>"))
        {
            if (!table.Value.Contains("股票代碼")) continue;
            foreach (Match row in Matches(table.Value, @"<tr\b[^>]*>(.*?)</tr>"))
            {
                var cells = Matches(row.Value, @"<td\b[^>]*>(.*?)</td>").Cast<Match>().Select(x => Clean(x.Groups[1].Value)).ToArray();
                if (cells.Length == 5 && Regex.IsMatch(cells[0], @"^[0-9A-Z]{4,12}$") && Decimal(cells[4]) is decimal weight && weight >= 0 && weight <= 100)
                    result.Holdings.Add(new(cells[0], cells[1], weight));
            }
        }
        result.Holdings = result.Holdings.DistinctBy(x => x.Symbol).OrderByDescending(x => x.Weight).ToList();
    }
    public async Task<EtfData> GetAsync(string symbol, string name, DateOnly today, CancellationToken ct)
    {
        var result = new EtfData();
        await Task.WhenAll(Nav(), Assets());
        return result;
        async Task Safe(Func<Task> action)
        {
            try { await action(); }
            catch (Exception ex) when (ex is HttpRequestException or JsonException or RegexMatchTimeoutException or InvalidOperationException or KeyNotFoundException or FormatException || ex is OperationCanceledException && !ct.IsCancellationRequested)
            { logger.LogWarning("ETF official data unavailable ({ErrorType}).", ex.GetType().Name); }
        }
        Task Nav() => Safe(async () =>
        {
            using var body = new FormUrlEncodedContent(new Dictionary<string,string> { ["id"]=symbol, ["startDate"]=today.AddDays(-30).ToString("yyyy/MM/dd"), ["endDate"]=today.ToString("yyyy/MM/dd"), ["type"]="fundPric" });
            using var response = await client.PostAsync("https://www.twse.com.tw/zh/ETFortune/ajaxEtfInfoChart", body, ct);
            response.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            if (!doc.RootElement.TryGetProperty("netPrice", out var rows) || rows.ValueKind != JsonValueKind.Array) return;
            var latest = rows.EnumerateArray().Where(x => x.TryGetProperty("date", out var dt) && DateOnly.TryParseExact(dt.GetString(), "yyyy/MM/dd", out var d) && d <= today)
                .OrderByDescending(x => x.GetProperty("date").GetString()).FirstOrDefault();
            if (latest.ValueKind != JsonValueKind.Object || !latest.TryGetProperty("count", out var n) || !n.TryGetDecimal(out var nav) || nav <= 0) return;
            result.NavDate = DateOnly.ParseExact(latest.GetProperty("date").GetString()!, "yyyy/MM/dd");
            result.Nav = nav;
            if (doc.RootElement.TryGetProperty("atmps", out var premiums) && premiums.ValueKind == JsonValueKind.Array)
                foreach (var row in premiums.EnumerateArray())
                    if (row.GetProperty("date").GetString() == latest.GetProperty("date").GetString() && row.GetProperty("count").TryGetDecimal(out var premium)) result.Premium = premium;
        });
        Task Assets() => Safe(async () =>
        {
            if (name.StartsWith("富邦", StringComparison.Ordinal))
            {
                result.HoldingsUrl = "https://websys.fsit.com.tw/FubonETF/Fund/Assets.aspx?stkId=" + Uri.EscapeDataString(symbol);
                ParseAssets(await client.GetStringAsync(result.HoldingsUrl, ct), result);
            }
            else
            {
                var html = await client.GetStringAsync("https://www.twse.com.tw/zh/ETFortune/etfInfo/" + Uri.EscapeDataString(symbol), ct);
                var m = Matches(html, @"資產規模\(億元\)\s*</p>\s*<span>([\d,.]+)</span>").Cast<Match>().FirstOrDefault();
                if (m != null) result.Assets = Decimal(m.Groups[1].Value) * 100000000m;
            }
        });
    }
}
