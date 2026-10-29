using System.Collections.Immutable;
using WorkJournal.Web.Models.V2;
namespace WorkJournal.Web.Models.Evidence;

public sealed record LinearBounds(decimal Lower, decimal Upper);
public sealed record Anchor(decimal X, decimal Y);
public enum VarianceDefinition { Population, Sample }
public enum AtrDefinition { SimpleMean, WilderSeededMean }
public enum PersistenceGroup { Foreign, Trust, Dealer, Combined }
// No default research horizons/bounds/statistical policy are invented. Null is a definition gap.
public sealed record V3EvidenceCandidateV1ParameterSet
{
    public string CandidateVersion { get; init; } = "V3EvidenceCandidate-v1";
    public int MaShort { get; init; } = 20;
    public int MaMedium { get; init; } = 60;
    public int MaLong { get; init; } = 120;
    public int? SlopeHorizon { get; init; }
    public int? StructureHorizon { get; init; }
    public LinearBounds? AlignmentBounds { get; init; }
    public LinearBounds? PositionBounds { get; init; }
    public LinearBounds? SlopeBounds { get; init; }
    public ImmutableArray<decimal> TrendWeights { get; init; } = [.30m,.25m,.25m,.20m];
    public ImmutableArray<decimal> DistanceWeights { get; init; } = [.6m,.4m];
    public ImmutableArray<decimal> MomentumWeights { get; init; } = [.40m,.35m,.25m];
    public ImmutableArray<Anchor> RsiAnchors { get; init; } = [new(30,.10m),new(40,.30m),new(50,.50m),new(60,.75m),new(70,1m),new(80,.75m),new(90,.40m)];
    public LinearBounds? MacdBounds { get; init; }
    public int? RocHorizon { get; init; }
    public LinearBounds? RocBounds { get; init; }
    public ImmutableArray<decimal> VolumePriceWeights { get; init; } = [.40m,.35m,.25m];
    public int? VolumeHorizon { get; init; }
    public int? ReturnHorizon { get; init; }
    public int? BreakoutHorizon { get; init; }
    public LinearBounds? RvolLogBounds { get; init; }
    public LinearBounds? PvcBounds { get; init; }
    public LinearBounds? BreakoutBounds { get; init; }
    public ImmutableArray<int> InstitutionalHorizons { get; init; } = [];
    public ImmutableArray<decimal> InstitutionalHorizonWeights { get; init; } = [.25m,.35m,.40m];
    public ImmutableArray<decimal> InstitutionalWeights { get; init; } = [.40m,.30m,.10m,.20m];
    public string FlowDenominator { get; init; } = "Shares/TradingVolumeShares";
    public int? PersistenceHorizon { get; init; }
    public PersistenceGroup? Persistence { get; init; }
    public ImmutableArray<int> RelativeStrengthHorizons { get; init; } = [];
    public ImmutableArray<decimal> RelativeStrengthWeights { get; init; } = [.25m,.35m,.40m];
    public string PercentileMethod { get; init; } = "StrictLessPlusHalfTies/N";
    public int? VolatilityHorizon { get; init; }
    public VarianceDefinition? Variance { get; init; }
    public int? DrawdownHorizon { get; init; }
    public int? AtrPeriod { get; init; }
    public AtrDefinition? Atr { get; init; }
    public int? LiquidityHorizon { get; init; }
    public string? LiquidityPrice { get; init; }
    public ImmutableArray<decimal> RiskWeights { get; init; } = [.30m,.30m,.20m,.20m];
}

// All volumes and flows are shares, prices use one explicitly versioned adjustment basis.
// Sessions must come from a historical trading calendar; they are not inferred from available bars.
public sealed record CandidateBar(DateOnly Date, decimal Close, decimal High, decimal Low, decimal Volume,
    DateTimeOffset? AvailableAt, EvidenceStatus Status = EvidenceStatus.Available);
public sealed record CandidateFlow(DateOnly Date, decimal Foreign, decimal Trust, decimal Dealer,
    DateTimeOffset? AvailableAt, EvidenceStatus Status = EvidenceStatus.Available);
public sealed record CandidateSeries(Guid InstrumentId, ImmutableArray<CandidateBar> Bars, ImmutableArray<CandidateFlow> Flows);
public sealed record CandidateIdentity(Guid DatasetSnapshotId, Guid UniverseSnapshotId, Guid BenchmarkVersionId,
    Guid VersionSetId, Guid ParameterSetId, string ParameterChecksum, string DatasetChecksum,
    string UniverseChecksum, string BenchmarkChecksum, string PriceBasisVersion, DateOnly TradeDate, DateTimeOffset DataCutoff);
public sealed record CandidateDataset(CandidateIdentity Identity, FoundationStatus Status,
    DateTimeOffset? UniverseAvailableAt, DateTimeOffset? BenchmarkMappingAvailableAt,
    ImmutableArray<DateOnly> Sessions, ImmutableArray<Guid> UniverseMembers,
    ImmutableArray<CandidateSeries> Series, ImmutableArray<CandidateBar> Benchmark);
public sealed record CandidateFeature(string Code, decimal RawValue, decimal NormalizedValue);
public sealed record CandidateComponent(NormalizedEvidence Evidence, ImmutableArray<CandidateFeature> Features);
public sealed record CandidateTrace(CandidateIdentity Identity, Guid InstrumentId, string CandidateVersion,
    string CalculationId, string InputDigest, EvidenceScore Score, ImmutableArray<CandidateComponent> Components)
{
    public string ValidationStatus => "RESEARCH_CANDIDATE_NOT_PRODUCTION_VERIFIED";
}
