using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WorkJournal.Web.Data;
using WorkJournal.Web.Models;
namespace WorkJournal.Web.Services;
public interface IStrategySignalSnapshotService
{
    StrategySignalSnapshot Create(StrategyFacts facts);
}
public class StrategySignalSnapshotService : IStrategySignalSnapshotService
{
    public StrategySignalSnapshot Create(StrategyFacts f)
    {
        var copy = JsonSerializer.Deserialize<StrategyFacts>(JsonSerializer.Serialize(f))!;
        copy.Input = f.Input with
        {
            Prices = f.Input.Prices.Where(x => x.Date <= f.Input.TradeDate).OrderBy(x => x.Date).TakeLast(120).ToArray(),
            Flows = f.Input.Flows.Where(x => x.Date <= f.Input.TradeDate).ToArray(),
            Sessions = f.Input.Sessions.Where(x => x <= f.Input.TradeDate).ToArray(),
            Events = f.Input.Events.Where(x => x.StockId == f.Input.Security.Symbol && x.PublishedAt <= f.Input.AvailableAt && DateOnly.FromDateTime(x.PublishedAt.ToOffset(TimeSpan.FromHours(8)).DateTime) <= f.Input.TradeDate).ToArray()
        };
        return new()
    {
        Close = f.Input.Prices.Single(x => x.Date == f.Input.TradeDate).Close!.Value,
        MarketHeat = f.Input.Heat, Industry = f.Input.Security.Industry,
        DataAvailableAt = f.Input.AvailableAt, Json = JsonSerializer.Serialize(copy)
    };
    }
}
public class StrategySignalService(JournalDbContext db, IStrategySignalSnapshotService snapshots, TimeProvider clock)
{
    public Task SaveAsync(long runId, ScanProfile profile, StrategyFacts facts, StrategyDecision decision, bool finalizeMissing, CancellationToken ct)
        => db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
    {
        db.ChangeTracker.Clear();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var key = $"Strategy:{profile.Id}:{facts.Input.Security.Symbol}:{decision.Code}:1";
        await db.Database.ExecuteSqlInterpolatedAsync($"DECLARE @r int; EXEC @r=sys.sp_getapplock @Resource={key},@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=15000; IF @r<0 THROW 51010,'Strategy busy',1;", ct);
        var date = facts.Input.TradeDate; var symbol = facts.Input.Security.Symbol;
        var evaluation = await db.StrategyEvaluations.SingleOrDefaultAsync(x => x.ProfileId == profile.Id && x.TradeDate == date && x.Symbol == symbol && x.StrategyCode == decision.Code && x.StrategyVersion == "1", ct);
        if (evaluation?.IsFinal == true) { await tx.CommitAsync(ct); return; }
        // Never retroactively splice an episode after a newer evaluation was finalized.
        if (await db.StrategyEvaluations.AnyAsync(x => x.ProfileId == profile.Id && x.Symbol == symbol && x.StrategyCode == decision.Code && x.IsFinal && x.TradeDate > date, ct))
            throw new InvalidOperationException("較晚的正式觀測已存在，歷史修订必須另建研究版本。");
        if (evaluation == null) { evaluation = new() { ProfileId = profile.Id, TradeDate = date, Symbol = symbol, StrategyCode = decision.Code }; db.Add(evaluation); }
        evaluation.RunId = runId; evaluation.Status = decision.Status; evaluation.EligibilityStatus = facts.Eligibility.Status;
        evaluation.Reason = decision.Reason; evaluation.CreatedAt = clock.GetUtcNow();
        evaluation.IsFinal = decision.Status != "InsufficientData" || finalizeMissing;
        if (evaluation.IsFinal)
        {
            var episode = await db.StrategyEpisodes.SingleOrDefaultAsync(x => x.ProfileId == profile.Id && x.Symbol == symbol && x.StrategyCode == decision.Code && x.StrategyVersion == "1" && x.EndedTradeDate == null, ct);
            if (episode != null)
            {
                var observed = await db.StrategyEvaluations.Where(x => x.ProfileId == profile.Id && x.Symbol == symbol && x.StrategyCode == decision.Code && x.IsFinal && x.Status == "Matched" && x.TradeDate > episode.StartedTradeDate && x.TradeDate < date).Select(x => x.TradeDate).ToArrayAsync(ct);
                var missing = facts.Input.Sessions.Count(x => x > episode.StartedTradeDate && x < date && !observed.Contains(x));
                episode.HasObservationGap |= missing > 0;
                // Count total known gap dates since last matched, avoiding repeated increments across retries.
                episode.GapDays = missing;
            }
            if (decision.Status == "Matched")
            {
                var isNew = episode == null;
                if (episode == null)
                {
                    var previousDate = facts.Input.Sessions.Where(x => x < date).Order().LastOrDefault();
                    var known = await db.StrategyEvaluations.AnyAsync(x => x.ProfileId == profile.Id && x.Symbol == symbol && x.StrategyCode == decision.Code && x.TradeDate == previousDate && x.IsFinal && (x.Status == "NotMatched" || x.Status == "NotEligible"), ct);
                    episode = new() { ProfileId = profile.Id, Symbol = symbol, StrategyCode = decision.Code, StartedTradeDate = date, EntrySignalLevel = decision.Level, PriorStateUnknown = !known };
                    db.Add(episode);
                }
                episode.LastMatchedDate = date; episode.ConfirmedDays++;
                var signal = new StrategySignal { Episode = episode, ProfileId = profile.Id, TradeDate = date, Symbol = symbol,
                    StockName = facts.Input.Security.Name, Market = facts.Input.Security.Market, StrategyCode = decision.Code,
                    SignalType = isNew ? "NEW" : "CONTINUE", SignalLevel = decision.Level, Score = facts.RadarScore,
                    Reason = decision.Reason, CreatedAt = clock.GetUtcNow(), Snapshot = snapshots.Create(facts),
                    Performance = isNew ? new() : null };
                db.Add(signal);
            }
            else if (episode != null && decision.Status is "NotMatched" or "NotEligible")
            { episode.EndedTradeDate = date; episode.EndReason = decision.Status == "NotEligible" ? facts.Eligibility.Reason : "StrategyNotMatched"; }
            else if (episode != null) { episode.HasObservationGap = true; episode.GapDays++; }
        }
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
    });
}
