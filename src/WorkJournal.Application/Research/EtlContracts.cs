using WorkJournal.Web.Models.HistoricalResearch;
namespace WorkJournal.Application.Research;

public sealed record ProviderCapabilities(string Provider,string Status,string[] Datasets,string SchemaVersion,string Access,string HistoricalAuthority,string[] Limitations);
public sealed record ProviderRequest(string Provider,string Dataset,string Instrument,DateOnly Date,DateOnly CoverageStart,DateOnly CoverageEnd,string SourceVersion,string TransformCodeSha,string SchemaVersion="HistoricalNormalized-v1");
public sealed record ProviderResponse(HistoricalFact[] Facts,string ParserVersion,string RawDigest,string SourceUrl,DateTimeOffset CapturedAt,bool Synthetic);
public sealed record QualityCheck(string Check,string Status,string Reason);
public sealed record QuarantinedRecord(int Row,string[] Reasons,string FactDigest);
public sealed record DataQualityReport(string Status,int InputRows,int AcceptedRows,QuarantinedRecord[] Quarantine,QualityCheck[] Checks,string Digest);
public sealed record EtlTransition(int Sequence,string State,DateTimeOffset At,string PreviousDigest,string Digest);
public sealed record EtlLineage(string BatchId,string RequestDigest,string Provider,string SourceUrl,string RawDigest,string RawArchiveId,
    DateTimeOffset CapturedAt,string SourceVersion,string SchemaVersion,string TransformCodeSha,string ParserVersion,string NormalizedDigest,string QualityDigest,string[] FactDigests,string Digest);
public sealed record EtlJobReport(string JobId,int Attempt,string Status,DateTimeOffset StartedAt,DateTimeOffset CompletedAt,ProviderRequest Request,
    bool Synthetic,int FactsCount,int LoadedFacts,DataQualityReport? Quality,EtlLineage? Lineage,EtlTransition[] Stages,string? ErrorCode,
    string AuthorityStatus,string PitStatus,string DatasetApproval,string ResearchStatus,string Digest);
public sealed record EtlBatch(ProviderRequest Request,SourceArchive Raw,HistoricalFact[] Facts,DataQualityReport Quality,EtlLineage Lineage,string Digest);
public sealed record EtlPublication(string SchemaVersion,DateTimeOffset VerifiedAt,ProviderCapabilities[] Providers,EtlJobReport[] Jobs,string ReplaySemantics,string Digest);
