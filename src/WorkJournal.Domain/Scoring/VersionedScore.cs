namespace WorkJournal.Domain.Scoring;
public sealed record ScoreComponent(string Code, int Weight, decimal? Points, string Status, string Reason);
public sealed record ScoreFeature(string Code, decimal RawValue, decimal NormalizedValue);
public sealed record VersionedScore(string ModelVersion, string FormulaDigest, string? ParameterSetId,
    decimal? TotalScore, int AvailableWeight, string ScoreStatus, ScoreComponent[] ComponentScores,
    ScoreFeature[] FeatureTrace, string? DatasetSnapshotId, DateTimeOffset? DataCutoff,
    string[] BlockingReasons, string ResearchStatus);
public sealed record ScoreComparison(string Mode, VersionedScore Legacy, VersionedScore Candidate,
    decimal? Difference, string BaselineStatus, string OosStatus, string PromotionStatus);
