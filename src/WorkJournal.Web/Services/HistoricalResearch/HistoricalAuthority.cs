using System.Collections.Immutable;
using System.Security.Cryptography;
using WorkJournal.Web.Models.HistoricalResearch;
using WorkJournal.Web.Models.Research;
using WorkJournal.Web.Services.Research;
namespace WorkJournal.Web.Services.HistoricalResearch;

public static class HistoricalAuthority
{
    public static string PolicyScopeDigest(HistoricalBuildRequest dataset,FrozenAvailabilityPolicy policy)=>
        ResearchMath.Digest(new{Availability=policy,Calendar=dataset.Calendar,PriceBasis=dataset.PricePolicy});
    public static void ValidateEvidence(HistoricalAvailabilityEvidence e)
    {
        if(e.EvidenceId==Guid.Empty||!Enum.IsDefined(e.EvidenceType)||!Enum.IsDefined(e.VerificationStatus)||!Enum.IsDefined(e.DatasetType)||
            new[]{e.Provider,e.SourceIdentity,e.EvidenceUriOrReference,e.ClaimedAvailabilityRule,e.Timezone,e.ReviewerNotes}.Any(string.IsNullOrWhiteSpace)||e.EffectiveTo<e.EffectiveFrom)
            throw new ArgumentException("Evidence identity, scope, rule, timezone and reviewer notes required.");
        byte[] bytes;
        try{bytes=Convert.FromBase64String(e.BodyBase64);TimeZoneInfo.FindSystemTimeZoneById(e.Timezone);}
        catch(Exception ex) when(ex is FormatException or TimeZoneNotFoundException or InvalidTimeZoneException){throw new ArgumentException("Invalid evidence body or timezone.",ex);}
        if(bytes.Length==0||Convert.ToHexString(SHA256.HashData(bytes))!=e.EvidenceDigest)throw new InvalidDataException("Evidence digest mismatch.");
    }
    public static HistoricalDataAuthority Transition(HistoricalDataAuthority a,AuthorityStatus next,DateTimeOffset at,string reviewer,string reason)
    {
        var allowed=(a.Status,next) switch
        {
            (AuthorityStatus.Draft,AuthorityStatus.EvidenceCollected)=>true,
            (AuthorityStatus.EvidenceCollected,AuthorityStatus.UnderReview)=>true,
            (AuthorityStatus.UnderReview,AuthorityStatus.Approved or AuthorityStatus.Rejected)=>true,
            (AuthorityStatus.Approved,AuthorityStatus.Superseded)=>true,
            _=>false
        };
        if(!allowed||string.IsNullOrWhiteSpace(reviewer)||string.IsNullOrWhiteSpace(reason)||at<a.ReviewedAt)
            throw new InvalidOperationException("Invalid review transition, timestamp or reviewer. New definitions require a new AuthorityId.");
        return a with{Status=next,ReviewedAt=at,ReviewedBy=reviewer,ReasonCode=reason};
    }
    public static AuthoritySubmission Canonicalize(AuthoritySubmission s)=>s with
    {
        Dataset=HistoricalDatasetBuilder.Canonicalize(s.Dataset),
        Authorities=s.Authorities.Select(a=>a with{EvidenceReferences=a.EvidenceReferences.Order().ToImmutableArray()}).OrderBy(a=>a.AuthorityId).ToImmutableArray(),
        Evidence=s.Evidence.OrderBy(e=>e.EvidenceId).ToImmutableArray(),Bindings=s.Bindings.OrderBy(b=>b.FactChecksum,StringComparer.Ordinal).ToImmutableArray()
    };
    public static string SubmissionDigest(AuthoritySubmission s)=>ResearchMath.Digest(Canonicalize(s) with{Review=null});
    public static AuthorityFactDecision Authorize(AuthoritySubmission s,HistoricalFact f)
    {
        AuthorityFactDecision Deny(string reason,Guid? id=null)=>new(f.FactChecksum,id,false,reason);
        var bindings=s.Bindings.Where(b=>b.FactChecksum==f.FactChecksum).ToArray();
        if(bindings.Length!=1)return Deny("AUTHORITY_BINDING_MISSING_OR_AMBIGUOUS");
        var binding=bindings[0];var authorities=s.Authorities.Where(a=>a.AuthorityId==binding.AuthorityId).ToArray();
        if(authorities.Length!=1)return Deny("AUTHORITY_MISSING_OR_AMBIGUOUS",binding.AuthorityId);
        var a=authorities[0];var archive=s.Dataset.Archives.SingleOrDefault(x=>x.ArchiveId==f.ArchiveId);
        var policy=s.Dataset.Policies.SingleOrDefault(x=>x.PolicyId==f.AvailabilityPolicyId);
        if(a.Status!=AuthorityStatus.Approved)return Deny("AUTHORITY_NOT_APPROVED",a.AuthorityId);
        if(a.ReviewedAt is null||a.ReviewedAt>s.EvaluatedAt||a.AuthorityId==Guid.Empty||a.EffectiveTo<a.EffectiveFrom||
            new[]{a.ReviewedBy,a.ReasonCode,a.RevisionPolicyVersion}.Any(string.IsNullOrWhiteSpace))return Deny("AUTHORITY_REVIEW_INVALID",a.AuthorityId);
        if(archive is null||policy is null||a.Provider!=f.Provider||a.DatasetType!=f.DatasetType||a.SourceIdentity!=archive.SourceUri||
            a.PolicyScopeDigest!=PolicyScopeDigest(s.Dataset,policy)||policy.FrozenAt>a.ReviewedAt||
            a.AvailabilityPolicyVersion!=policy.Version||a.RevisionPolicyVersion!=binding.RevisionPolicyVersion||
            a.CalendarPolicyVersion!=s.Dataset.Calendar.Version||a.PriceBasisVersion!=s.Dataset.PricePolicy.PriceBasisVersion||
            f.TradeDate<a.EffectiveFrom||f.TradeDate>a.EffectiveTo)return Deny("AUTHORITY_SCOPE_MISMATCH",a.AuthorityId);
        if(a.EvidenceReferences.IsDefaultOrEmpty||a.EvidenceReferences.Distinct().Count()!=a.EvidenceReferences.Length)return Deny("AUTHORITY_EVIDENCE_MISSING",a.AuthorityId);
        foreach(var id in a.EvidenceReferences)
        {
            var entries=s.Evidence.Where(e=>e.EvidenceId==id).ToArray();
            if(entries.Length!=1)return Deny("AUTHORITY_EVIDENCE_MISSING_OR_AMBIGUOUS",a.AuthorityId);
            var e=entries[0];
            try{ValidateEvidence(e);}catch(Exception ex) when(ex is ArgumentException or InvalidDataException){return Deny("AUTHORITY_EVIDENCE_INVALID",a.AuthorityId);}
            if(e.VerificationStatus!=EvidenceVerificationStatus.Verified||e.EvidenceCapturedAt>a.ReviewedAt||e.Provider!=a.Provider||e.DatasetType!=a.DatasetType||
                e.SourceIdentity!=a.SourceIdentity||e.Timezone!=policy.Timezone||e.ClaimedAvailabilityRule!=policy.Version||
                f.TradeDate<e.EffectiveFrom||f.TradeDate>e.EffectiveTo)return Deny("AUTHORITY_EVIDENCE_UNVERIFIED_OR_OUT_OF_SCOPE",a.AuthorityId);
        }
        // Authority is additional to temporal eligibility, never a replacement for it.
        if(HistoricalAvailability.Resolve(f,policy).At is null)return Deny("AVAILABILITY_STILL_UNPROVEN",a.AuthorityId);
        return new(f.FactChecksum,a.AuthorityId,true,"APPROVED_AUTHORITY_AND_VERIFIED_AVAILABILITY");
    }
    public static AuthorityDatasetReport Evaluate(AuthoritySubmission submission)
    {
        var s=Canonicalize(submission);var digest=SubmissionDigest(s);
        if(s.EvaluatedAt<s.Dataset.CreatedAt||s.Authorities.Select(a=>a.AuthorityId).Distinct().Count()!=s.Authorities.Length||
            s.Evidence.Select(e=>e.EvidenceId).Distinct().Count()!=s.Evidence.Length||s.Bindings.Select(b=>b.FactChecksum).Distinct().Count()!=s.Bindings.Length)
            throw new ArgumentException("Duplicate authority/evidence/binding identities or evaluation before capture.");
        var data=new HistoricalDatasetBuilder().Build(s.Dataset);
        var eligible=data.Decisions.Where(d=>d.Eligible).Select(d=>d.FactChecksum).ToHashSet();
        var decisions=s.Dataset.Facts.Where(f=>eligible.Contains(f.FactChecksum)).Select(f=>Authorize(s,f)).ToImmutableArray();
        var gaps=new List<ResearchGap>();
        if(s.Authorities.Length==0)gaps.Add(new("AUTHORITY_GAP","No reviewed historical authority supplied."));
        if(data.Snapshots.Length==0||data.Quality.Coverage.Any(c=>c.Coverage!=1)||data.Gaps.Any(g=>g.Code=="SURVIVORSHIP_BIAS_RISK"))
            gaps.Add(new("DATASET_NOT_READY","Complete historical coverage and PIT snapshots required for this initial approval scope."));
        foreach(var g in decisions.Where(d=>!d.Authorized).GroupBy(d=>d.ReasonCode))gaps.Add(new("AUTHORITY_GAP",g.Key+": "+g.Count()));
        var review=s.Review;
        if(review is null||review.Status!=AuthorityStatus.Approved||review.SubmissionDigest!=digest||review.ReviewedAt>s.EvaluatedAt||
            review.ReviewedAt<s.Dataset.CreatedAt||s.Authorities.Any(a=>a.ReviewedAt>review.ReviewedAt)||
            string.IsNullOrWhiteSpace(review.ReviewedBy)||string.IsNullOrWhiteSpace(review.ReasonCode))
            gaps.Add(new("DATASET_APPROVAL_REQUIRED","Explicit reviewer approval must bind this exact submission digest; no provider-based auto approval."));
        if(s.Dataset.Archives.Any(a=>a.Synthetic))gaps.Add(new("SYNTHETIC_ONLY","Synthetic sources cannot become approved real research data."));
        var report=new AuthorityDatasetReport(digest,"",gaps.Count==0?"APPROVED_PIT_DATASET":"BLOCKED_BY_AUTHORITY_OR_DATA",data,decisions,gaps.ToImmutableArray(),review);
        return report with{ReportDigest=ResearchMath.Digest(report)};
    }
}
