using System.Text.Json;
using WorkJournal.Web.Models.HistoricalResearch;
using WorkJournal.Web.Models.Research;
using WorkJournal.Web.Services.Research;
namespace WorkJournal.Web.Services.HistoricalResearch;

// Operator-owned local audit store. Reviewer identity is an attestation, not an external digital signature.
public sealed class AuthorityStore(string root)
{
    public void Evidence(HistoricalAvailabilityEvidence evidence)
    {
        HistoricalAuthority.ValidateEvidence(evidence);
        var dir=Path.Combine(root,"evidence");Directory.CreateDirectory(dir);
        HistoricalDatasetStore.WriteNew(Path.Combine(dir,evidence.EvidenceId.ToString("N")+".json"),new{Evidence=evidence,Digest=ResearchMath.Digest(evidence)});
    }
    public HistoricalAvailabilityEvidence ReadEvidence(Guid id)
    {
        using var doc=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"evidence",id.ToString("N")+".json")));
        var e=doc.RootElement.GetProperty("Evidence").Deserialize<HistoricalAvailabilityEvidence>()!;
        HistoricalAuthority.ValidateEvidence(e);
        if(e.EvidenceId!=id||doc.RootElement.GetProperty("Digest").GetString()!=ResearchMath.Digest(e))throw new InvalidDataException("Evidence metadata digest mismatch.");
        return e;
    }
    public AuthorityRevision Register(HistoricalDataAuthority authority)
    {
        if(authority.AuthorityId==Guid.Empty||authority.Status!=AuthorityStatus.Draft||authority.ReviewedAt is not null||
           authority.EvidenceReferences.IsDefault||authority.EffectiveTo<authority.EffectiveFrom||
           new[]{authority.Provider,authority.SourceIdentity,authority.AvailabilityPolicyVersion,authority.RevisionPolicyVersion,authority.CalendarPolicyVersion,authority.PriceBasisVersion,authority.ReasonCode,authority.PolicyScopeDigest}.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("Register a complete Draft definition with a new AuthorityId; no initial approval.");
        var dir=Path.Combine(root,"authorities",authority.AuthorityId.ToString("N"));Directory.CreateDirectory(dir);
        return Append(dir,0,"",authority);
    }
    public AuthorityRevision Review(Guid id,string expectedDigest,AuthorityStatus next,DateTimeOffset at,string reviewer,string reason)
    {
        var current=Read(id);
        if(current.Digest!=expectedDigest)throw new InvalidOperationException("Authority changed; review current revision first.");
        var updated=HistoricalAuthority.Transition(current.Authority,next,at,reviewer,reason);
        if(next==AuthorityStatus.Approved)
        {
            if(updated.EvidenceReferences.IsDefaultOrEmpty)throw new InvalidOperationException("Evidence required before approval.");
            foreach(var reference in updated.EvidenceReferences)
            {
                var e=ReadEvidence(reference);
                if(e.VerificationStatus!=EvidenceVerificationStatus.Verified||e.EvidenceCapturedAt>at||e.Provider!=updated.Provider||e.DatasetType!=updated.DatasetType||e.SourceIdentity!=updated.SourceIdentity)
                    throw new InvalidOperationException("Unverified, future or mismatched approval evidence.");
            }
        }
        return Append(Path.Combine(root,"authorities",id.ToString("N")),current.Sequence+1,current.Digest,updated);
    }
    private static AuthorityRevision Append(string directory,int sequence,string previous,HistoricalDataAuthority authority)
    {
        var revision=new AuthorityRevision(sequence,previous,authority,"");revision=revision with{Digest=ResearchMath.Digest(revision)};
        // CreateNew on sequence makes concurrent review writes conflict rather than silently overwrite.
        HistoricalDatasetStore.WriteNew(Path.Combine(directory,sequence.ToString("D8",System.Globalization.CultureInfo.InvariantCulture)+".json"),revision);
        return revision;
    }
    public AuthorityRevision Read(Guid id)
    {
        var files=Directory.GetFiles(Path.Combine(root,"authorities",id.ToString("N")),"*.json").Order(StringComparer.Ordinal).ToArray();
        AuthorityRevision? prior=null;
        for(int i=0;i<files.Length;i++)
        {
            var r=HistoricalDatasetStore.Read<AuthorityRevision>(files[i]);
            if(r.Sequence!=i||r.Authority.AuthorityId!=id||r.PreviousDigest!=(prior?.Digest??"")||r.Digest!=ResearchMath.Digest(r with{Digest=""}))throw new InvalidDataException("Authority history digest/sequence mismatch.");
            if(prior is null)
            {if(r.Authority.Status!=AuthorityStatus.Draft||r.Authority.ReviewedAt is not null)throw new InvalidDataException("Authority must begin Draft.");}
            else if(r.Authority.ReviewedAt is null||ResearchMath.Digest(HistoricalAuthority.Transition(prior.Authority,r.Authority.Status,r.Authority.ReviewedAt.Value,r.Authority.ReviewedBy,r.Authority.ReasonCode))!=ResearchMath.Digest(r.Authority))
                throw new InvalidDataException("Authority definition changed during review.");
            prior=r;
        }
        return prior??throw new InvalidDataException("No authority history.");
    }
    public AuthorityDatasetReport EvaluateAndSave(AuthoritySubmission submission)
    {
        // Submitted authority/evidence must match the durable current registry, preventing stale superseded approvals.
        foreach(var a in submission.Authorities)
            if(ResearchMath.Digest(Read(a.AuthorityId).Authority)!=ResearchMath.Digest(a))throw new InvalidDataException("Submission authority is not current registry revision.");
        foreach(var e in submission.Evidence)
            if(ResearchMath.Digest(ReadEvidence(e.EvidenceId))!=ResearchMath.Digest(e))throw new InvalidDataException("Submission evidence differs from archive.");
        var s=HistoricalAuthority.Canonicalize(submission);var report=HistoricalAuthority.Evaluate(s);
        var dir=Path.Combine(root,"datasets");Directory.CreateDirectory(dir);
        // Same submission may have pending and reviewed evaluations; bind storage to the complete report digest.
        HistoricalDatasetStore.WriteNew(Path.Combine(dir,report.ReportDigest+".json"),new AuthorityArchive(s,report));return report;
    }
    public AuthorityArchive Replay(string digest,bool requireCurrent=false)
    {
        if(digest.Length!=64||!digest.All(Uri.IsHexDigit))throw new ArgumentException("SHA256 report digest required.");
        var saved=HistoricalDatasetStore.Read<AuthorityArchive>(Path.Combine(root,"datasets",digest+".json"));
        var report=HistoricalAuthority.Evaluate(saved.Submission);
        if(report.ReportDigest!=digest||ResearchMath.Digest(report)!=ResearchMath.Digest(saved.Report))throw new InvalidDataException("Authority archive replay mismatch.");
        if(requireCurrent)
        {
            foreach(var a in saved.Submission.Authorities)
                if(ResearchMath.Digest(Read(a.AuthorityId).Authority)!=ResearchMath.Digest(a))throw new InvalidOperationException("Authority superseded or changed; new research approval required.");
            foreach(var e in saved.Submission.Evidence)
                if(ResearchMath.Digest(ReadEvidence(e.EvidenceId))!=ResearchMath.Digest(e))throw new InvalidDataException("Current evidence archive differs from approved submission.");
        }
        return saved;
    }
    public string Research(string digest,ResearchRequest template,string researchRoot)
    {
        var saved=Replay(digest,true);
        if(saved.Report.Status!="APPROVED_PIT_DATASET")throw new InvalidOperationException("BLOCKED_BY_AUTHORITY_OR_DATA");
        if(template.Snapshots.Length!=0)throw new ArgumentException("Template snapshots must be empty.");
        var request=template with{Snapshots=saved.Report.Dataset.Snapshots};
        return new ResearchStore(researchRoot).Execute(request,new ApprovedResearchAuthorization(saved));
    }
}
public sealed record AuthorityArchive(AuthoritySubmission Submission,AuthorityDatasetReport Report);

// Cannot be constructed by the CLI or external callers; issued only after approved archive replay.
public sealed class ApprovedResearchAuthorization
{
    internal ApprovedResearchAuthorization(AuthorityArchive archive){Archive=archive;}
    public AuthorityArchive Archive { get; }
    public void Validate(ResearchRequest request)
    {
        var s=Archive.Submission;var report=HistoricalAuthority.Evaluate(s);
        if(report.Status!="APPROVED_PIT_DATASET"||ResearchMath.Digest(report)!=ResearchMath.Digest(Archive.Report)||request.CreatedAt<s.Review!.ReviewedAt||
            ResearchMath.Digest(request.Snapshots)!=ResearchMath.Digest(report.Dataset.Snapshots))throw new InvalidOperationException("Research approval scope mismatch.");
        if(!request.Outcomes.Sessions.SequenceEqual(s.Dataset.Calendar.Sessions)||request.Outcomes.EvaluationCutoff>s.EvaluatedAt)throw new InvalidOperationException("Outcome calendar or approval cutoff outside reviewed source scope.");
        // Future labels remain separate from feature snapshots but also require authorized source facts.
        var labelRequest=s.Dataset with{Dates=[new(request.Outcomes.Sessions.Max(),request.Outcomes.EvaluationCutoff)]};
        var eligibility=new HistoricalDatasetBuilder().Build(labelRequest).Decisions.Where(d=>d.Eligible).ToArray();
        foreach(var series in request.Outcomes.Series)foreach(var close in series.Closes)
        {
            var matches=eligibility.Where(d=>d.InstrumentId==series.InstrumentId).Select(d=>s.Dataset.Facts.Single(f=>f.FactChecksum==d.FactChecksum))
                .Where(f=>f.DatasetType==HistoricalDatasetType.Price&&f.TradeDate==close.Date&&f.Value.Close==close.Close&&f.PriceBasisVersion==series.PriceBasisVersion&&
                    HistoricalAvailability.Resolve(f,s.Dataset.Policies.SingleOrDefault(p=>p.PolicyId==f.AvailabilityPolicyId)).At==close.AvailableAt).ToArray();
            if(matches.Length!=1||!HistoricalAuthority.Authorize(s,matches[0]).Authorized)throw new InvalidOperationException("Future outcome lacks reviewed PIT source provenance.");
        }
    }
}
