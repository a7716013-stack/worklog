using WorkJournal.Web.Models.V2;
namespace WorkJournal.Web.Models.Evidence;

public enum EvidenceCode { Trend, Momentum, VolumePrice, Institutional, RelativeStrength, RiskQuality }
public enum EvidenceScoreStatus { Complete, Incomplete }
public enum DefinitionStatus { Verified, Candidate, Unresolved, Blocked }
// Representation only. No method is selected or implemented as a production policy.
public enum NormalizationMethod { LinearBounded, Piecewise, Percentile, InversePercentile, LogBounded, ZScore, ZScoreToCdf }
public sealed record NormalizationDefinition(string Code, string Version, NormalizationMethod Method,
    Guid ParameterSetId, DefinitionStatus Status);
public sealed record FeatureDefinition(string Code, string Version, EvidenceCode Domain, string InputDefinition,
    string LookbackDefinition, NormalizationDefinition? Normalization, DefinitionStatus Status);
public sealed record FeatureValue(string Code, string Version, decimal? Value, EvidenceStatus Status,
    string ReasonCode, Guid DatasetSnapshotId, Guid ProvenanceId, DateTimeOffset EffectiveAt,
    DateTimeOffset? AvailableAt, DateTimeOffset DataCutoff);

// Low-level aggregation input, not a raw-data calculator or a production scoring endpoint.
public sealed record NormalizedEvidence(EvidenceCode Code, decimal? Value, EvidenceStatus Status,
    string ReasonCode, string FeatureVersion, string NormalizationVersion);
public sealed record EvidenceContribution(EvidenceCode Code, decimal? NormalizedValue, int Weight,
    decimal? WeightedPoints, EvidenceStatus Status, string ReasonCode, string FeatureVersion, string NormalizationVersion);
public sealed record EvidenceScore(EvidenceScoreStatus Status, decimal? TotalScore, int AvailableWeight,
    string AggregationVersion, IReadOnlyList<EvidenceContribution> Components);
public sealed record FullEvidenceRequest(Guid InstrumentId, DateOnly TradeDate, DateTimeOffset DataCutoff,
    Guid DatasetSnapshotId, Guid UniverseSnapshotId, Guid VersionSetId, Guid ParameterSetId);
public sealed record ModelDefinitionGap(EvidenceCode Evidence, string ReasonCode, string MissingDefinition);
public sealed record EvidencePreparation(FullEvidenceRequest Request, Guid BenchmarkVersionId,
    string ModelVersion, string FeatureVersion, string ParameterChecksum,
    IReadOnlyList<DataProvenance> PitInputs, IReadOnlyList<ModelDefinitionGap> Gaps)
{
    public string Status => "MODEL_DEFINITION_GAP";
    public decimal? TotalScore => null;
    public Guid? DecisionTraceId => null;
}
