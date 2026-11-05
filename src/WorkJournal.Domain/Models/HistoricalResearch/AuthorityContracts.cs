using System.Collections.Immutable;
using WorkJournal.Web.Models.Research;
namespace WorkJournal.Web.Models.HistoricalResearch;

public enum AuthorityStatus { Draft, EvidenceCollected, UnderReview, Approved, Rejected, Superseded }
public enum AvailabilityEvidenceType { OfficialSpecification, OfficialAnnouncement, OfficialArchiveMetadata, TimestampedArchive, ProviderDocumentation }
public enum EvidenceVerificationStatus { Unverified, Verified, Rejected }
public sealed record HistoricalAvailabilityEvidence(Guid EvidenceId,string Provider,HistoricalDatasetType DatasetType,string SourceIdentity,
    AvailabilityEvidenceType EvidenceType,string EvidenceUriOrReference,DateTimeOffset EvidenceCapturedAt,string EvidenceDigest,
    string ClaimedAvailabilityRule,DateOnly EffectiveFrom,DateOnly? EffectiveTo,string Timezone,
    EvidenceVerificationStatus VerificationStatus,string ReviewerNotes,string BodyBase64);
public sealed record HistoricalDataAuthority(Guid AuthorityId,string Provider,HistoricalDatasetType DatasetType,string SourceIdentity,
    string AvailabilityPolicyVersion,string RevisionPolicyVersion,string CalendarPolicyVersion,string PriceBasisVersion,
    DateOnly EffectiveFrom,DateOnly? EffectiveTo,ImmutableArray<Guid> EvidenceReferences,
    DateTimeOffset? ReviewedAt,string ReviewedBy,AuthorityStatus Status,string ReasonCode,string PolicyScopeDigest);
public sealed record AuthorityRevision(int Sequence,string PreviousDigest,HistoricalDataAuthority Authority,string Digest);
public sealed record AuthorityFactBinding(string FactChecksum,Guid AuthorityId,string RevisionPolicyVersion);
public sealed record DatasetApprovalReview(string SubmissionDigest,AuthorityStatus Status,DateTimeOffset ReviewedAt,string ReviewedBy,string ReasonCode);
public sealed record AuthoritySubmission(HistoricalBuildRequest Dataset,ImmutableArray<HistoricalDataAuthority> Authorities,
    ImmutableArray<HistoricalAvailabilityEvidence> Evidence,ImmutableArray<AuthorityFactBinding> Bindings,DateTimeOffset EvaluatedAt,
    DatasetApprovalReview? Review);
public sealed record AuthorityFactDecision(string FactChecksum,Guid? AuthorityId,bool Authorized,string ReasonCode);
public sealed record AuthorityDatasetReport(string SubmissionDigest,string ReportDigest,string Status,HistoricalBuildResult Dataset,
    ImmutableArray<AuthorityFactDecision> Decisions,ImmutableArray<ResearchGap> Gaps,DatasetApprovalReview? Review)
{
    public string ModelStatus=>"RESEARCH_CANDIDATE_NOT_PRODUCTION_VERIFIED";
}
