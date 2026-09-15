using System.Globalization;
using System.Text.Json;
using WorkJournal.Web.Models;
namespace WorkJournal.Web.Services;

public partial class FinMindStockService
{
    private async Task<FundamentalData> GetFundamentalsAsync(string symbol, DateOnly today, CancellationToken ct)
    {
        var data = new FundamentalData();
        try
        {
            var valuations = await ReadAsync("TaiwanStockPER", symbol, today.AddDays(-90), today, ct);
            var latest = valuations.Where(x => Text(x, "stock_id") == symbol && ParseDate(x) is DateOnly d && d <= today)
                .OrderByDescending(ParseDate).FirstOrDefault();
            if (latest.ValueKind != JsonValueKind.Undefined)
            {
                data.ValuationDate = ParseDate(latest);
                data.PE = Positive(Number(latest, "PER"));
                data.PB = Positive(Number(latest, "PBR"));
                var yield = Number(latest, "dividend_yield");
                data.Yield = yield >= 0 ? yield : null;
                data.ValuationMessage = "FinMind 估值資料；無有效估值的欄位顯示 —。";
            }
            else data.ValuationMessage = "近 90 日無估值資料；ETF 等商品可能不適用。";
        }
        catch (Exception ex) when (OptionalFailure(ex, ct)) { data.ValuationMessage = "估值資料暫時無法取得，行情與技術指標仍可使用。"; }
        try
        {
            var revenues = await ReadAsync("TaiwanStockMonthRevenue", symbol, today.AddMonths(-16), today, ct);
            var periods = revenues.Where(x => Text(x, "stock_id") == symbol && RevenuePeriod(x) != null &&
                    RevenuePeriod(x) < new DateOnly(today.Year, today.Month, 1))
                .GroupBy(x => RevenuePeriod(x)!.Value)
                .ToDictionary(x => x.Key, x => x.OrderByDescending(v => Text(v, "create_time")).ThenByDescending(ParseDate).First());
            if (periods.Count > 0)
            {
                var month = periods.Keys.Max();
                data.RevenueMonth = month;
                data.Revenue = Number(periods[month], "revenue");
                data.RevenueYoY = periods.TryGetValue(month.AddYears(-1), out var year) ? Growth(data.Revenue, Number(year, "revenue")) : null;
                data.RevenueMoM = periods.TryGetValue(month.AddMonths(-1), out var previous) ? Growth(data.Revenue, Number(previous, "revenue")) : null;
                data.RevenueMessage = "依營收歸屬月份計算；年增／月增比較去年同月及上月。";
            }
            else data.RevenueMessage = "無月營收資料；ETF 等商品不適用公司營收指標。";
        }
        catch (Exception ex) when (OptionalFailure(ex, ct)) { data.RevenueMessage = "月營收暫時無法取得，其他資料仍可使用。"; }
        return data;
    }
    private static bool OptionalFailure(Exception ex, CancellationToken ct) =>
        ex is HttpRequestException or JsonException or InvalidDataException ||
        ex is OperationCanceledException && !ct.IsCancellationRequested;
    private static decimal? Positive(decimal? value) => value > 0 ? value : null;
    private static decimal? Growth(decimal? value, decimal? baseline) => baseline > 0 ? (value / baseline - 1) * 100 : null;
    private static DateOnly? ParseDate(JsonElement row) =>
        DateOnly.TryParseExact(Text(row, "date"), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null;
    private static DateOnly? RevenuePeriod(JsonElement row)
    {
        var year = Number(row, "revenue_year"); var month = Number(row, "revenue_month");
        return year is >= 1900 and <= 9999 && month is >= 1 and <= 12 && year == decimal.Truncate(year.Value) && month == decimal.Truncate(month.Value)
            ? new DateOnly((int)year.Value, (int)month.Value, 1) : null;
    }
}
