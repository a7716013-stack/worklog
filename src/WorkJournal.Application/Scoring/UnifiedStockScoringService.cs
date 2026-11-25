using WorkJournal.Domain.Scoring;
using WorkJournal.Domain.Research;
using WorkJournal.Web.Models;
using WorkJournal.Web.Models.Evidence;
using WorkJournal.Web.Services;
using WorkJournal.Web.Services.Evidence;
namespace WorkJournal.Application.Scoring;

public interface IUnifiedStockScoringService
{
    MarketRadarStock Radar(MarketQuote quote, RadarHistory history, MarketEvent[] events);
    SwingAnalysis Swing(SwingHistory history);
    ScoreComparison Compare(VersionedScore legacy, string mode = "Legacy", bool authorizedResearchRole = false, bool researchEnabled = false);
    VersionedScore CandidateFixture(CandidateDataset dataset, Guid instrument, V3EvidenceCandidateV1ParameterSet parameters);
    BacktestComparison Backtest(SwingHistory history, DateOnly start, decimal fee = .001425m, decimal sellTax = .003m);
}
public sealed record BacktestComparison(string LegacyModelVersion, SwingBacktest Legacy, string CandidateModelVersion,
    string CandidateBaselineStatus, string CandidateOosStatus, string PromotionStatus, string[] BlockingReasons);

// No provider, HTTP client, approval mutation, scheduler or live-data adapter is reachable here.
public sealed class UnifiedStockScoringService : IUnifiedStockScoringService
{
    public const string RadarVersion = "LegacyRadar-v1";
    public const string SwingVersion = "LegacySwing75-v1";
    public const string RadarDigest = "F5085CB268C89D1BE694E1A20355377607B1746390EE345E4C9808D17CDBDCC2";
    public const string SwingDigest = "9CD341BBD2E56713CAA554EB71DCD6ABDFD85A94A34F3D59CDF3A47E43FD6454";
    public MarketRadarStock Radar(MarketQuote quote, RadarHistory history, MarketEvent[] events)
    {
        var result = MarketRadarScoreCalculator.Calculate(quote, history, events);
        result.Scoring = Compare(RadarScore(result));
        return result;
    }
    public static VersionedScore RadarScore(MarketRadarStock value, DateTimeOffset? cutoff = null) => new(
        RadarVersion, RadarDigest, "LegacyRadar-fixed-v1", value.Score, value.Available,
        value.Score.HasValue ? "Complete" : "Incomplete",
        value.Parts.Select(p => new ScoreComponent(p.Name, p.Maximum, p.Points, p.Points.HasValue ? "Available" : "Missing", p.Explanation)).ToArray(),
        [], null, cutoff, value.Parts.Where(p => !p.Points.HasValue).Select(p => p.Name + ": " + p.Explanation).ToArray(), "LEGACY_NOT_CANDIDATE_VALIDATION");
    public SwingAnalysis Swing(SwingHistory history)
    {
        var summary = SwingCalculator.Calculate(history.Bars, history.Flows);
        var legacy = new VersionedScore(SwingVersion, SwingDigest, "LegacySwing-fixed-v1", summary.Score, summary.Available,
            summary.Score.HasValue ? "Complete" : "Incomplete",
            summary.Rules.Select(p => new ScoreComponent(p.Label, p.Points, p.Passed.HasValue ? (p.Passed.Value ? p.Points : 0) : null,
                p.Passed.HasValue ? "Available" : "Missing", p.Group)).ToArray(), [], null, null,
            summary.Rules.Where(p => !p.Passed.HasValue).Select(p => p.Label).ToArray(), "LEGACY_NOT_CANDIDATE_VALIDATION");
        return new(history.Stock, summary, SwingCalculator.Chart(history.Bars), history.Message) { Scoring = Compare(legacy) };
    }
    public ScoreComparison Compare(VersionedScore legacy, string mode = "Legacy", bool authorizedResearchRole = false, bool researchEnabled = false)
    {
        if (mode is not ("Legacy" or "Shadow" or "Preview" or "CandidatePrimary")) throw new ArgumentException("UNKNOWN_MODEL_MODE");
        if (mode == "CandidatePrimary") throw new InvalidOperationException("CANDIDATE_NOT_PROMOTED");
        if (mode != "Legacy" && !authorizedResearchRole) throw new UnauthorizedAccessException("RESEARCH_ROLE_REQUIRED");
        var reason = mode == "Legacy" ? "CANDIDATE_NOT_REQUESTED" : !researchEnabled ? "RESEARCH_FEATURE_DISABLED" : "NO_APPROVED_SIX_COMPONENT_DATASET";
        // No approved source is configured for 3.3.1. Fail closed even when the feature flag is on.
        return new(mode, legacy, Blocked(reason), null, "NOT_RUN", "NOT_RUN", "NOT_PROMOTED");
    }
    public static VersionedScore Blocked(string reason) => new(V3CandidateCalculator.Version, FormulaCatalog.Identity.FormulaDigest,
        null, null, 0, "Incomplete", FullEvidenceModel.Weights.Select(p => new ScoreComponent(p.Key.ToString(), p.Value, null, "Missing", reason)).ToArray(),
        [], null, null, [reason], "BLOCKED_BY_DATA");
    // Internal/CLI test seam only. Never exposed as an HTTP raw-data route or promoted to real validation.
    public VersionedScore CandidateFixture(CandidateDataset dataset, Guid instrument, V3EvidenceCandidateV1ParameterSet parameters)
    {
        var trace = new V3CandidateCalculator().Calculate(dataset, instrument, parameters);
        return new(trace.CandidateVersion, FormulaCatalog.Identity.FormulaDigest, trace.Identity.ParameterSetId.ToString(), trace.Score.TotalScore,
            trace.Score.AvailableWeight, trace.Score.Status.ToString(), trace.Score.Components.Select(p => new ScoreComponent(p.Code.ToString(), p.Weight,
                p.WeightedPoints, p.Status.ToString(), p.ReasonCode)).ToArray(),
            trace.Components.SelectMany(c => c.Features).Select(f => new ScoreFeature(f.Code, f.RawValue, f.NormalizedValue)).ToArray(),
            trace.Identity.DatasetSnapshotId.ToString(), trace.Identity.DataCutoff,
            trace.Score.Components.Where(p => p.WeightedPoints is null).Select(p => p.ReasonCode).ToArray(), "SYNTHETIC_FIXTURE_NOT_REAL_VALIDATION");
    }
    public BacktestComparison Backtest(SwingHistory history, DateOnly start, decimal fee = .001425m, decimal sellTax = .003m) =>
        new(SwingVersion, SwingBacktester.Run(history, start, fee, sellTax), V3CandidateCalculator.Version,
            "NOT_RUN", "NOT_RUN", "NOT_PROMOTED", ["NO_APPROVED_SIX_COMPONENT_DATASET"]);
}
