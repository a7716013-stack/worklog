using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WorkJournal.Web.Data;
using WorkJournal.Web.Models;
using WorkJournal.Web.ViewModels;
namespace WorkJournal.Web.Services;

// Read-only projections. No account creation, fills, synchronization or snapshot capture.
public class HomeDashboardService(JournalDbContext db, ILogger<HomeDashboardService> logger)
{
    public static DateOnly Today => DateOnly.FromDateTime(DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(8)).DateTime);

    private async Task<T?> Read<T>(Func<CancellationToken, Task<T>> query, CancellationToken ct) where T : class
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(8));
        try { return await query(timeout.Token); }
        catch (Exception e) when (!ct.IsCancellationRequested)
        { logger.LogWarning("Dashboard widget unavailable ({Type})", e.GetType().Name); return null; }
    }

    public Task<DashboardWorkSummary?> WorkAsync(string userId, DateOnly day, CancellationToken ct) => Read(async token =>
    {
        var owned = db.WorkLogs.AsNoTracking().Where(x => x.ApplicationUserId == userId && x.WorkDate == day);
        return new DashboardWorkSummary(await owned.CountAsync(token), await owned.CountAsync(x => x.Status == WorkStatus.Completed, token),
            await owned.OrderBy(x => x.Status == WorkStatus.Completed).ThenBy(x => x.StartTime).ThenByDescending(x => x.Id).Take(5).ToListAsync(token));
    }, ct);

    public async Task<DashboardInvestmentSummary> InvestmentAsync(string userId, CancellationToken ct)
    {
        // Sequential DB operations: a scoped EF context must not be used concurrently.
        var watch = await Read(async token => new CountValue(await db.StockWatchlistItems.CountAsync(x => x.ApplicationUserId == userId, token)), ct);
        var tracking = await Read(async token => new CountValue(await db.MarketRadarPersonalTrackings.CountAsync(x => x.ApplicationUserId == userId && x.StoppedAt == null, token)), ct);
        var accounts = db.PaperTradingAccounts.Where(x => x.ApplicationUserId == userId).Select(x => x.Id);
        var positions = await Read(async token => new CountValue(await db.PaperPositions.CountAsync(x => accounts.Contains(x.AccountId) && x.Quantity > 0, token)), ct);
        var orders = await Read(async token => new CountValue(await db.PaperOrders.CountAsync(x => accounts.Contains(x.AccountId) && x.Status == PaperOrderStatus.Pending, token)), ct);
        return new(watch?.Value, tracking?.Value, positions?.Value, orders?.Value);
    }

    public Task<CalendarAggregation?> CalendarAsync(string userId, DateOnly day, CalendarAggregationService calendar, CancellationToken ct) => Read(async token =>
    {
        var logs = await db.WorkLogs.AsNoTracking().Where(x => x.ApplicationUserId == userId && x.WorkDate == day).ToListAsync(token);
        var result = await calendar.GetAsync(userId, new DateOnly(day.Year, day.Month, 1), logs, token);
        return new CalendarAggregation(result.Events.Where(x => x.OccursOn(day)).OrderBy(x => x.Start).ToList(), result.Warning);
    }, ct);

    public Task<MarketRadarResult?> MarketAsync(IMarketRadarService radar, CancellationToken ct) =>
        Read(token => radar.GetAsync("all", null, false, token), ct); // Existing provider cache; never score on Home.

    public Task<DashboardRadarSummary?> RadarAsync(CancellationToken ct) => Read(async token =>
    {
        // Only public daily selections, never private manually followed recommendations.
        var day = await db.MarketRadarDailySelections.MaxAsync(x => (DateOnly?)x.TradeDate, token);
        var rows = await db.MarketRadarDailySelections.AsNoTracking().Where(x => x.TradeDate == day)
            .OrderBy(x => x.Rank).Select(x => x.Recommendation).ToListAsync(token);
        var top = rows.Take(5).Select(x => new DashboardRadarRow(x.StockId, x.StockName, x.Score, Level(x))).ToArray();
        var industries = rows.Where(x => !x.IsEtf && !string.IsNullOrWhiteSpace(x.Industry) && x.Industry != "未知")
            .GroupBy(x => x.Industry).Select(x => new DashboardIndustry(x.Key, x.Count())).OrderByDescending(x => x.Count).ThenBy(x => x.Name).Take(5).ToArray();
        return new DashboardRadarSummary(day, rows.Count, top, industries);
    }, ct);

    private static string Level(MarketRadarRecommendation row)
    {
        try { return JsonSerializer.Deserialize<MarketRadarStock>(row.SnapshotJson)?.Level ?? "尚未提供"; }
        catch (JsonException) { return "尚未提供"; }
    }
    private record CountValue(int Value);
}
