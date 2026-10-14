using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WorkJournal.Web.Data;
using WorkJournal.Web.Models;
namespace WorkJournal.Web.Services;
public interface IStrategyPerformanceService { Task<int> CalculatePendingPerformanceAsync(CancellationToken ct); }
public class StrategyPerformanceService(JournalDbContext db, IStrategyDataProvider source, TimeProvider clock) : IStrategyPerformanceService
{
    public async Task<int> CalculatePendingPerformanceAsync(CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(clock.GetUtcNow().ToOffset(TimeSpan.FromHours(8)).DateTime);
        var pending = await db.StrategySignals.Include(x => x.Snapshot).Include(x => x.Performance)
            .Where(x => x.Performance != null && !x.Performance.Completed && x.TradeDate < today).OrderBy(x => x.TradeDate).Take(500).ToListAsync(ct);
        if (pending.Count == 0) return 0;
        var calendar = await source.CalendarAsync(ct);
        var through = clock.GetUtcNow().ToOffset(TimeSpan.FromHours(8)).Hour >= 14 ? today : today.AddDays(-1);
        var count = 0;
        foreach (var group in pending.GroupBy(x => x.Symbol))
        {
            StrategyPrice[] prices;
            try { prices = await source.PricesAsync(group.Key, through, ct); }
            catch (StrategyRateLimitException) { break; }
            catch (Exception e) when (TaiwanMarketRankingProvider.Failure(e, ct)) { continue; }
            foreach (var signal in group)
            {
                var rows = StrategyPerformanceCalculator.Calculate(signal.TradeDate, signal.Snapshot.Close, calendar, prices, through, out var open, out var date);
                var p = signal.Performance!;
                var existing = JsonSerializer.Deserialize<StrategyHorizon[]>(p.Json) ?? [];
                // Mature values stay as observed; fill pending/unavailable fields without silently revising completed observations.
                rows = rows.Select(row => existing.FirstOrDefault(x => x.Days == row.Days) is { } old
                    ? row with { CloseReturn = old.CloseReturn ?? row.CloseReturn, OpenReturn = old.OpenReturn ?? row.OpenReturn,
                        CloseMfe = old.CloseMfe ?? row.CloseMfe, CloseMae = old.CloseMae ?? row.CloseMae,
                        OpenMfe = old.OpenMfe ?? row.OpenMfe, OpenMae = old.OpenMae ?? row.OpenMae,
                        CloseStatus = old.CloseStatus == "Complete" ? old.CloseStatus : row.CloseStatus,
                        OpenStatus = old.OpenStatus == "Complete" ? old.OpenStatus : row.OpenStatus } : row).ToArray();
                p.NextOpenDate ??= date; p.NextOpen ??= open;
                p.IsReferenceOnly = date.HasValue && signal.CreatedAt >= new DateTimeOffset(date.Value.ToDateTime(new TimeOnly(9, 0)), TimeSpan.FromHours(8));
                p.Json = JsonSerializer.Serialize(rows); p.UpdatedAt = clock.GetUtcNow();
                p.Completed = rows.All(x => x.CloseStatus == "Complete" && x.OpenStatus == "Complete" && x.CloseMfe.HasValue && x.OpenMfe.HasValue);
                count++;
            }
            await db.SaveChangesAsync(ct);
        }
        return count;
    }
}
