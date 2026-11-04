using System.Collections.Immutable;
using WorkJournal.Web.Models.Evidence;
using WorkJournal.Web.Models.V2;
namespace WorkJournal.Web.Models.Research;

public enum ResearchRunStatus { Created, Prepared, Running, Completed, Failed, Rejected, Promoted }
public enum ResearchPartition { Train, Validation, Oos }
public sealed record ResearchPeriod(DateOnly Start, DateOnly End);
public sealed record ResearchFold(string Code, ResearchPeriod Train, ResearchPeriod Validation, ResearchPeriod Oos);
public sealed record ResearchProtocol(string Version, ImmutableArray<ResearchFold> Folds,
    ImmutableArray<int> OutcomeHorizons, int Quantiles, int EmbargoSessions,
    string PurgeRule = "LabelEndWithinPartition-v1", string QuantileRule = "MidrankKeepTies-v1",
    string DistributionRule = "LinearOrderStatistic-v1", string AblationRule = "SubtractNoRedistribution-v1");
public sealed record DataAvailabilityPolicy(string Provider, string DatasetType, string PolicyVersion,
    DateOnly EffectiveFrom, DateOnly? EffectiveTo, string AvailabilityRule, string EvidenceSource);
public sealed record ResearchSourceEvidence(string Reference, string HistoricalUniverseEvidence,
    string HistoricalBenchmarkEvidence, string AvailabilityEvidence, string RevisionEvidence,
    string PriceBasisEvidence, bool Synthetic);
public sealed record ResearchSnapshot(CandidateDataset Data, ResearchSourceEvidence Evidence);
// Outcomes are a separate sidecar. They are NEVER passed to the Candidate calculator.
public sealed record OutcomeClose(DateOnly Date, decimal Close, DateTimeOffset? AvailableAt, EvidenceStatus Status);
public sealed record OutcomeSeries(Guid InstrumentId, string PriceBasisVersion, ImmutableArray<OutcomeClose> Closes);
public sealed record OutcomeDataset(string SnapshotId, string RevisionVersion, DateTimeOffset EvaluationCutoff,
    ImmutableArray<DateOnly> Sessions, ImmutableArray<OutcomeSeries> Series);
public sealed record ResearchGroup(Guid InstrumentId, DateOnly Date, string Kind, string Value,
    string DefinitionVersion, DateTimeOffset? AvailableAt);
public sealed record SelectionReceipt(Guid DevelopmentRunId, string Fold, Guid ParameterSetId, string ParameterChecksum,
    string CandidateVersion, string DevelopmentDigest, DateTimeOffset SelectedAt, string Rationale);
public sealed record ResearchRequest(Guid ResearchRunId, DateTimeOffset CreatedAt,
    string CandidateVersion, string ModelVersion, string FeatureVersion,
    V3EvidenceCandidateV1ParameterSet Parameters, ResearchProtocol Protocol,
    ImmutableArray<ResearchSnapshot> Snapshots, OutcomeDataset Outcomes,
    ImmutableArray<DataAvailabilityPolicy> AvailabilityPolicies, ImmutableArray<ResearchGroup> Groups,
    bool EvaluateOos = false, ImmutableArray<SelectionReceipt> Selections = default);
public sealed record ResearchOutcome(int Horizon, DateOnly? LabelEnd, decimal? FutureReturn,
    decimal? FutureMaxCloseReturn, decimal? FutureMinCloseReturn, string Status);
public sealed record ResearchObservation(Guid ResearchRunId, CandidateTrace Trace, ImmutableArray<ResearchOutcome> Outcomes);
public sealed record ResearchValue(DateOnly Date, Guid InstrumentId, decimal? Value, decimal? Outcome, int? Quantile);
public sealed record ResearchDistribution(int Count, decimal? Min, decimal? P01, decimal? P05, decimal? P10,
    decimal? P25, decimal? P50, decimal? P75, decimal? P90, decimal? P95, decimal? P99,
    decimal? Max, decimal? Mean, decimal? StdDev, decimal MissingRate);
public sealed record DailyIc(DateOnly Date, int Count, decimal? PearsonIc, decimal? RankIc);
public sealed record QuantileResult(int Quantile, int Count, decimal? MeanReturn, decimal? MedianReturn, decimal? PositiveRate);
public sealed record ResearchMetric(string Fold, ResearchPartition Partition, string Variable, int Horizon,
    int ObservationCount, int AvailableCount, int OutcomePairCount, decimal Coverage, decimal MissingRate,
    int PurgedCount, decimal? MeanFutureReturn, decimal? MedianFutureReturn,
    ImmutableArray<DailyIc> DailyIc, decimal? MeanIc, decimal? MeanRankIc, decimal? MedianRankIc,
    decimal? StdDevRankIc, decimal? PositiveRankIcRatio, ImmutableArray<QuantileResult> Quantiles,
    decimal? TopMinusBottomSpread, decimal? AdjacentMonotonicity, decimal? TurnoverProxy,
    ResearchDistribution Distribution);
public sealed record ResearchCorrelation(string Fold, ResearchPartition Partition, string Left, string Right,
    ImmutableArray<DailyIc> Daily);
public sealed record GroupMetric(string Kind, string Value, string DefinitionVersion, ResearchMetric Metric);
public sealed record ResearchGap(string Code, string Detail);
public sealed record ComponentResearchStatus(string Evidence, string Status, string Reason);
public sealed record ResearchReport(Guid ResearchRunId, string ResearchInputDigest, string ResearchOutputDigest,
    ResearchRunStatus Status, bool Finalized, ImmutableArray<ResearchObservation> Observations,
    ImmutableArray<ResearchMetric> Metrics, ImmutableArray<ResearchCorrelation> Correlations,
    ImmutableArray<GroupMetric> GroupMetrics, ImmutableArray<ResearchGap> Gaps,
    ImmutableArray<ComponentResearchStatus> ComponentStatus, string PromotionStatus)
{
    public string ModelStatus => "RESEARCH_CANDIDATE_NOT_PRODUCTION_VERIFIED";
}
// This view deliberately cannot carry OOS metrics into the selection workflow.
public sealed record DevelopmentScorecard(Guid ResearchRunId, Guid ParameterSetId, string ParameterChecksum,
    string CandidateVersion, string Fold, DateTimeOffset OutcomesKnownAt, string InputDigest,
    ImmutableArray<ResearchMetric> Train, ImmutableArray<ResearchMetric> Validation, string Digest);
