using System.Diagnostics;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using WorkJournal.Web.Data;
using WorkJournal.Web.Models;
namespace WorkJournal.Web.Services;
public class StrategyScanService(JournalDbContext db, IStrategyDataProvider source, IStockUniverseService universe,
    StrategySignalService signals, IOptions<StrategyScanOptions> options, TimeProvider clock, ILogger<StrategyScanService> logger)
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    public async Task<StrategyScanRun?> RunAsync(CancellationToken ct)
    {
        if (!await Gate.WaitAsync(0, ct)) return null;
        var watch = Stopwatch.StartNew(); StrategyScanRun? run = null;
        try
        {
            var p = options.Value.Profile;
            if (!p.IsValid()) throw new InvalidOperationException("策略掃描 Profile 無效。");
            var profile = await db.ScanProfiles.AsNoTracking().SingleOrDefaultAsync(x => x.Code == p.Code && x.Version == p.Version, ct);
            if (profile == null) { profile = JsonSerializer.Deserialize<ScanProfile>(JsonSerializer.Serialize(p))!; profile.Id = 0; db.Add(profile); await db.SaveChangesAsync(ct); }
            else
            {
                var expected = JsonSerializer.Deserialize<ScanProfile>(JsonSerializer.Serialize(p))!;
                expected.Id = profile.Id; expected.CreatedAt = profile.CreatedAt; expected.IsActive = profile.IsActive;
                if (typeof(ScanProfile).GetProperties().Any(x => !Equals(x.GetValue(expected), x.GetValue(profile)))) throw new InvalidOperationException("設定已改變但 Profile 版本相同，請升版。");
            }
            if (!profile.IsActive) return null;
            var now = clock.GetUtcNow().ToOffset(TimeSpan.FromHours(8)); var today = DateOnly.FromDateTime(now.DateTime);
            run = new() { ProfileId = profile.Id, TradeDate = today, StartedAt = clock.GetUtcNow() }; db.Add(run); await db.SaveChangesAsync(ct);
            var calendar = await source.CalendarAsync(ct);
            var securities = await source.SecuritiesAsync(ct);
            var session = calendar.Contains(today);
            var ready = session && now.TimeOfDay >= new TimeSpan(13, 40, 0);
            var date = ready ? today : calendar.Where(x => x < today).Max();
            // Catalog membership is captured only for current-day signals, not reconstructed for yesterday.
            var candidates = securities.Where(x => x.Cfi.StartsWith("ES") && (x.Market == "twse" && profile.IncludeTWSE || x.Market == "tpex" && profile.IncludeTPEx)).ToList();
            var active = await db.StrategyEpisodes.AsNoTracking().Where(x => x.ProfileId == profile.Id && x.EndedTradeDate == null).Select(x => x.Symbol).Distinct().ToArrayAsync(ct);
            foreach (var missing in active.Except(candidates.Select(x => x.Symbol)))
                candidates.Add(new(missing, missing, "", "", "ES", false));
            run.UniverseCount = securities.Length;
            var evaluated = ready ? await db.StrategyEvaluations.AsNoTracking().Where(x => x.ProfileId == profile.Id && x.TradeDate == today).ToListAsync(ct) : [];
            var finished = evaluated.GroupBy(x => x.Symbol).Where(g => g.Count(x => x.IsFinal) == StrategyEngine.Codes.Length).Select(g => g.Key).ToHashSet();
            var order = evaluated.GroupBy(x => x.Symbol).ToDictionary(g => g.Key, g => g.Max(x => x.CreatedAt));
            var pending = candidates.Where(x => !finished.Contains(x.Symbol)).OrderBy(x => order.GetValueOrDefault(x.Symbol)).ThenBy(x => x.Symbol).ToArray();
            // Preparation rotates using persisted source keys rather than repeatedly warming the first batch.
            if (!ready)
            {
                var cached = await db.StrategySourceCaches.AsNoTracking().Where(x => x.Key.StartsWith("prices:")).Select(x => x.Key).ToArrayAsync(ct);
                pending = pending.Where(x => !cached.Contains($"prices:{x.Symbol}:{date:yyyyMMdd}")).ToArray();
            }
            var processed = 0; var failures = new List<string>();
            foreach (var security in pending.Take(options.Value.BatchSize))
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    if (!ready) { await source.PricesAsync(security.Symbol, date, ct); processed++; continue; }
                    StrategyInput input;
                    if (!security.NormalTrading) input = new(security, today, [], [], calendar, null, null, [], false, null, clock.GetUtcNow());
                    else input = await source.LoadAsync(security, today, calendar, ct);
                    var facts = StrategyEngine.Calculate(input, profile, universe);
                    if (facts.Eligibility.Status == "Eligible")
                    {
                        var daily = await db.StrategyDailyStocks.SingleOrDefaultAsync(x => x.ProfileId == profile.Id && x.TradeDate == today && x.Symbol == security.Symbol, ct);
                        if (daily == null) { daily = new() { ProfileId = profile.Id, TradeDate = today, Symbol = security.Symbol, StockName = security.Name, Market = security.Market }; db.Add(daily); }
                        daily.RadarScore = facts.RadarScore; daily.SwingLevel = facts.SwingLevel; daily.Json = JsonSerializer.Serialize(facts); daily.UpdatedAt = clock.GetUtcNow();
                        await db.SaveChangesAsync(ct);
                    }
                    foreach (var code in StrategyEngine.Codes)
                        await signals.SaveAsync(run.Id, profile, facts, StrategyEngine.Evaluate(code, facts, profile), now.Hour >= options.Value.CutoffHour, ct);
                    processed++;
                }
                catch (StrategyRateLimitException e) { failures.Add(e.Message); break; }
                catch (Exception e) when (TaiwanMarketRankingProvider.Failure(e, ct)) { failures.Add(security.Symbol + "：" + e.GetType().Name); }
            }
            db.ChangeTracker.Clear();
            if (ready)
            {
                var all = await db.StrategyEvaluations.AsNoTracking().Where(x => x.ProfileId == profile.Id && x.TradeDate == today).ToListAsync(ct);
                run.QualifiedCount = all.Where(x => x.EligibilityStatus == "Eligible").Select(x => x.Symbol).Distinct().Count();
                run.SkippedCount = all.Where(x => x.Status == "NotEligible" || x.IsFinal && x.Status == "InsufficientData").Select(x => x.Symbol).Distinct().Count();
                run.PendingCount = candidates.Count - all.GroupBy(x => x.Symbol).Count(g => g.Count(x => x.IsFinal) == StrategyEngine.Codes.Length);
                run.HitCount = all.Count(x => x.Status == "Matched" && x.StrategyCode != "SwingCandidate");
                run.RecommendationCount = all.Count(x => x.Status == "Matched" && x.StrategyCode == "SwingCandidate");
                run.Status = run.PendingCount == 0 && failures.Count == 0 ? "Complete" : "Partial";
            }
            else { run.PendingCount = Math.Max(0, pending.Length - processed); run.Status = session ? "PreparingHistory" : "MarketClosed"; }
            run.Message = failures.Count > 0 ? string.Join("；", failures.Take(5)) : ready ? "分批掃描；未完成股票下次續跑。" : "尚未收盤／休市，不建立訊號；僅準備歷史行情。";
            return run;
        }
        catch (Exception e) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning("Strategy scan deferred: {Type}", e.GetType().Name);
            if (run != null) { run.Status = "Failed"; run.Message = e is StrategyRateLimitException or InvalidOperationException or InvalidDataException ? e.Message : $"來源或資料庫暫不可用（{e.GetType().Name}）"; }
            return run;
        }
        finally
        {
            try
            {
            if (run != null && !ct.IsCancellationRequested)
            {
                db.ChangeTracker.Clear(); run.ApiCalls = source.ApiCalls; run.CacheHits = source.CacheHits; run.ElapsedMilliseconds = watch.ElapsedMilliseconds; run.CompletedAt = clock.GetUtcNow();
                db.Update(run); await db.SaveChangesAsync(ct);
                logger.LogInformation("Strategy run {Run}: {Status}, qualified {Qualified}, pending {Pending}, calls {Calls}, elapsed {Ms}ms", run.Id, run.Status, run.QualifiedCount, run.PendingCount, run.ApiCalls, run.ElapsedMilliseconds);
            }
            }
            finally { Gate.Release(); }
        }
    }
}
