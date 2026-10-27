using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace WorkJournal.MarketData;

public static class OfficialParser
{
    public static string Text(JsonElement x, string field) => x.TryGetProperty(field, out var v) ? v.ToString().Trim() : "";
    public static decimal? Number(string s) => decimal.TryParse(s.Replace(",", "").Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var n) ? n : null;
    public static DateOnly? Date(string s)
    {
        var digits = Regex.Replace(s, "[^0-9]", "");
        if (digits.Length == 7) digits = (int.Parse(digits[..3]) + 1911) + digits[3..];
        return DateOnly.TryParseExact(digits, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
    }
    public static JsonElement[] Catalog(string html, string market)
    {
        var rows = new List<JsonElement>();
        foreach (Match row in Regex.Matches(html, @"<tr\b[^>]*>(.*?)</tr>", RegexOptions.Singleline | RegexOptions.IgnoreCase))
        {
            var cells = Regex.Matches(row.Value, @"<td\b[^>]*>(.*?)</td>", RegexOptions.Singleline | RegexOptions.IgnoreCase)
                .Select(x => WebUtility.HtmlDecode(Regex.Replace(x.Groups[1].Value, "<[^>]+>", "")).Trim()).ToArray();
            if (cells.Length < 7 || cells[1].Length != 12 || cells[5].Length != 6) continue;
            var pair = Regex.Split(cells[0], @"\s+");
            if (pair.Length < 2 || !Regex.IsMatch(pair[0], @"^[0-9]{4}[0-9A-Z]{0,2}$")) continue;
            // Equity and collective investment products, not warrants or bonds.
            if (!cells[5].StartsWith("ES") && !cells[5].StartsWith("CE")) continue;
            rows.Add(JsonSerializer.SerializeToElement(new { stock_id = pair[0], stock_name = string.Join(" ", pair.Skip(1)),
                type = market, industry_category = cells[5].StartsWith("CE") ? "ETF" : cells[4], date = "2000-01-01", _source = "ISIN" }));
        }
        if (rows.Count == 0) throw new InvalidDataException("官方證券清單格式異常。");
        return rows.ToArray();
    }
    private static (string[] fields, JsonElement[] rows) Table(JsonElement root, string required)
    {
        var candidates = root.TryGetProperty("tables", out var tables) && tables.ValueKind == JsonValueKind.Array
            ? tables.EnumerateArray().ToArray() : [root];
        foreach (var t in candidates)
        {
            if (!t.TryGetProperty("fields", out var f) || !t.TryGetProperty("data", out var d) || f.ValueKind != JsonValueKind.Array || d.ValueKind != JsonValueKind.Array) continue;
            var fields = f.EnumerateArray().Select(x => x.ToString().Replace(" ", "")).ToArray();
            if (fields.Contains(required)) return (fields, d.EnumerateArray().ToArray());
        }
        var status = Text(root, "stat");
        if (status.Contains("沒有符合") || status.Contains("查無資料") || status.Contains("無資料")) return ([], []);
        throw new InvalidDataException("官方資料欄位異常；未將錯誤視為空資料。");
    }
    public static JsonElement[] MonthlyPrices(JsonElement root, string symbol, string market, DateOnly month)
    {
        if (NoData(root)) return [];
        if (Date(Text(root, "date")) is not { } responseDate || responseDate.Year != month.Year || responseDate.Month != month.Month)
            throw new InvalidDataException("官方月行情日期不符。");
        var (fields, rows) = Table(root, "日期");
        string Cell(JsonElement row, string field)
        {
            var i = Array.IndexOf(fields, field);
            if (i < 0 || row.GetArrayLength() <= i) throw new InvalidDataException("官方月行情欄位缺漏。");
            return row[i].ToString();
        }
        var result = new List<JsonElement>();
        foreach (var row in rows)
        {
            var date = Date(Cell(row, "日期"));
            if (date == null || date.Value.Year != month.Year || date.Value.Month != month.Month) throw new InvalidDataException("月行情包含非預期日期。");
            var otc = market == "tpex";
            // TPEx monthly volume is rounded lots and excludes block trades. Never fabricate exact shares.
            result.Add(JsonSerializer.SerializeToElement(new { stock_id = symbol, date = date.Value.ToString("yyyy-MM-dd"),
                open = Number(Cell(row, otc ? "開盤" : "開盤價")), max = Number(Cell(row, otc ? "最高" : "最高價")),
                min = Number(Cell(row, otc ? "最低" : "最低價")), close = Number(Cell(row, otc ? "收盤" : "收盤價")),
                spread = Number(Cell(row, otc ? "漲跌" : "漲跌價差")),
                Trading_Volume = otc ? (decimal?)null : Number(Cell(row, "成交股數")),
                Trading_money = otc ? (decimal?)null : Number(Cell(row, "成交金額")),
                _source = otc ? "TPEx.Monthly.PriceOnly" : "TWSE.Monthly" }));
        }
        return result.ToArray();
    }
    public static JsonElement[] DailyPrices(JsonElement root, DateOnly day)
    {
        if (NoData(root)) return [];
        if (Date(Text(root, "date")) != day) throw new InvalidDataException("官方日行情日期不符。");
        var (fields, rows) = Table(root, "成交股數");
        string Cell(JsonElement row, string name)
        {
            var i = Array.IndexOf(fields, name);
            if (i < 0 || row.GetArrayLength() <= i) throw new InvalidDataException("官方日行情欄位缺漏。");
            return row[i].ToString().Trim();
        }
        return rows.Select(row => JsonSerializer.SerializeToElement(new { stock_id = Cell(row, "代號"), date = day.ToString("yyyy-MM-dd"),
            open = Number(Cell(row,"開盤")), max = Number(Cell(row,"最高")), min = Number(Cell(row,"最低")), close = Number(Cell(row,"收盤")),
            spread = Number(Cell(row,"漲跌")), Trading_Volume = Number(Cell(row,"成交股數")), Trading_money = Number(Cell(row,"成交金額(元)")), _source = "TPEx.Daily" })).ToArray();
    }
    public static JsonElement[] Flows(JsonElement root, string market, DateOnly day)
    {
        if (NoData(root)) return [];
        if (Date(Text(root,"date")) != day) throw new InvalidDataException("官方法人資料日期不符。");
        var (fields, rows) = Table(root, market == "twse" ? "證券代號" : "代號");
        var result = new List<JsonElement>();
        foreach (var row in rows)
        {
            if (row.ValueKind != JsonValueKind.Array || row.GetArrayLength() < fields.Length) throw new InvalidDataException("官方法人資料不完整。");
            int Index(string field) { var i = Array.IndexOf(fields,field); return i >= 0 ? i : throw new InvalidDataException("官方法人欄位變更。"); }
            // TPEx repeats headings for its grouped columns; validate the entire expected group shape.
            if (market == "tpex" && (fields.Length != 24 || fields[23] != "三大法人買賣超股數合計" ||
                Enumerable.Range(0,7).Any(i => fields[2+i*3] != "買進股數" || fields[3+i*3] != "賣出股數" || fields[4+i*3] != "買賣超股數")))
                throw new InvalidDataException("上櫃法人群組欄位變更。");
            var symbol = row[market == "twse" ? Index("證券代號") : 0].ToString().Trim();
            var groups = market == "twse"
                ? new[] { ("Foreign_Investor", Index("外陸資買進股數(不含外資自營商)"), Index("外陸資賣出股數(不含外資自營商)")), ("Investment_Trust",Index("投信買進股數"),Index("投信賣出股數")), ("Dealer_self",Index("自營商買進股數(自行買賣)"),Index("自營商賣出股數(自行買賣)")), ("Dealer_Hedging",Index("自營商買進股數(避險)"),Index("自營商賣出股數(避險)")) }
                : new[] { ("Foreign_Investor",2,3), ("Investment_Trust",11,12), ("Dealer",20,21) };
            foreach (var (name,buy,sell) in groups)
                result.Add(JsonSerializer.SerializeToElement(new { stock_id = symbol, date = day.ToString("yyyy-MM-dd"), name,
                    buy = Number(row[buy].ToString()), sell = Number(row[sell].ToString()), _source = market == "twse" ? "TWSE.T86" : "TPEx.Institutional" }));
        }
        return result.ToArray();
    }
    private static bool NoData(JsonElement root)
    {
        var status = Text(root,"stat");
        return status.Contains("沒有符合") || status.Contains("查無資料");
    }
}
