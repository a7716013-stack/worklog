namespace WorkJournal.Web.Models.V2;

public enum FoundationStatus { Draft, Finalized, Failed }
public enum EvidenceStatus { Available, Missing, NotApplicable, Invalid, Stale }
public enum UniverseEligibility { Eligible, Ineligible, Unknown }
public enum DecisionKind { Research, ScoreEvidence, StrategySetup, StrategyEligibility, TriggerEvent, TradeDecision }
public interface IFoundationDocument
{
    Guid Id { get; set; }
    string Checksum { get; set; }
    FoundationStatus Status { get; set; }
    DateTimeOffset CreatedAt { get; set; }
}
public sealed class DatasetSnapshot : IFoundationDocument
{
    public Guid Id { get; set; }
    public string Checksum { get; set; } = "";
    public FoundationStatus Status { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateOnly AsOfDate { get; set; }
    public DateTimeOffset DataCutoff { get; set; }
    public string DatasetVersion { get; set; } = "";
    public string SourceSummary { get; set; } = "";
    public List<DataProvenance> Records { get; set; } = [];
}
public sealed class DataProvenance
{
    public Guid Id { get; set; }
    public Guid DatasetSnapshotId { get; set; }
    public Guid InstrumentId { get; set; }
    public string Provider { get; set; } = "";
    public string Dataset { get; set; } = "";
    public string SourceKey { get; set; } = "";
    public DateTimeOffset EffectiveAt { get; set; }
    // Earliest verifiable public/source availability, not period end. Unknown stays null.
    public DateTimeOffset? AvailableAt { get; set; }
    // First ingestion into our system versus retrieval of this particular source representation.
    public DateTimeOffset ObservedAt { get; set; }
    public DateTimeOffset RetrievedAt { get; set; }
    public EvidenceStatus QualityStatus { get; set; }
    public string PayloadJson { get; set; } = "{}";
    public string Checksum { get; set; } = "";
    public string? RawReference { get; set; }
}
public sealed class UniverseSnapshot : IFoundationDocument
{
    public Guid Id { get; set; }
    public string Checksum { get; set; } = "";
    public FoundationStatus Status { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateOnly TradeDate { get; set; }
    public DateTimeOffset DataCutoff { get; set; }
    public string UniverseVersion { get; set; } = "";
    public List<UniverseSnapshotMember> Members { get; set; } = [];
}
public sealed class UniverseSnapshotMember
{
    public Guid UniverseSnapshotId { get; set; }
    public Guid InstrumentId { get; set; }
    public UniverseEligibility EligibilityStatus { get; set; }
    public string ReasonCode { get; set; } = "";
}
public sealed class BenchmarkDefinition
{
    public Guid Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
}
public sealed class BenchmarkVersion
{
    public Guid Id { get; set; }
    public Guid BenchmarkDefinitionId { get; set; }
    public string Version { get; set; } = "";
    public string DefinitionJson { get; set; } = "{}";
    public DateTimeOffset ValidFrom { get; set; }
    public DateTimeOffset? ValidTo { get; set; }
}
public sealed class BenchmarkMapping
{
    public Guid Id { get; set; }
    public Guid InstrumentId { get; set; }
    public Guid BenchmarkVersionId { get; set; }
    public DateTimeOffset ValidFrom { get; set; }
    public DateTimeOffset? ValidTo { get; set; }
}
public sealed class VersionSet : IFoundationDocument
{
    public Guid Id { get; set; }
    public string Checksum { get; set; } = "";
    public FoundationStatus Status { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string ModelVersion { get; set; } = "";
    public string FeatureVersion { get; set; } = "";
    public string UniverseVersion { get; set; } = "";
    public Guid BenchmarkVersionId { get; set; }
    public string DataVersion { get; set; } = "";
    public string? StrategyVersion { get; set; }
    public string ExtensionsJson { get; set; } = "{}";
}
public sealed class ParameterSet : IFoundationDocument
{
    public Guid Id { get; set; }
    public string Checksum { get; set; } = "";
    public FoundationStatus Status { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string Code { get; set; } = "";
    public string Version { get; set; } = "";
    public string ParametersJson { get; set; } = "{}";
}
public sealed class DecisionTrace : IFoundationDocument
{
    public Guid Id { get; set; }
    public string Checksum { get; set; } = "";
    public FoundationStatus Status { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid InstrumentId { get; set; }
    public DateOnly TradeDate { get; set; }
    public DateTimeOffset DecisionTime { get; set; }
    public DateTimeOffset DataCutoff { get; set; }
    public Guid DatasetSnapshotId { get; set; }
    public Guid UniverseSnapshotId { get; set; }
    public Guid VersionSetId { get; set; }
    public Guid ParameterSetId { get; set; }
    public DecisionKind DecisionType { get; set; }
    public string DecisionStatus { get; set; } = "";
    public string ReasonCode { get; set; } = "";
    public string DetailsJson { get; set; } = "{}";
}
// Contract only: no score calculator, threshold, strategy or order policy.
public sealed record EvidenceValue(decimal? Value, EvidenceStatus Status, string Reason,
    DateTimeOffset? AvailableAt, Guid? ProvenanceId, string FeatureVersion);
