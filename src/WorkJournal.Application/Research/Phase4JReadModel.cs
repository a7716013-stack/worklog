using WorkJournal.Web.Services.Research;
using WorkJournal.Web.Services.HistoricalResearch;
namespace WorkJournal.Application.Research;

public sealed record SourceCapability(string SourceId,string Provider,string OfficialUrl,string SourceVersion,string AcquisitionMethod,
    string[] SupportedClaims,string[] CandidateClaims,string TemporalCoverage,string PublicationEvidenceCapability,string RevisionEvidenceCapability,
    string LicenseStatus,string AccessStatus,DateTimeOffset LastVerifiedAt,Guid[] EvidenceIds);
public sealed record AcquisitionWork(string TaskId,string ClaimKey,string SubjectDigest,string[] Instruments,DateOnly CoverageStart,DateOnly CoverageEnd,
    int Priority,string SourceId,string SourceVersion,string ExpectedEvidence,string ExecutionMode,string Status,int Attempts,string[] BlockingReasons,string NextAction);
public sealed record AcquisitionAttemptView(Guid AttemptId,string TaskId,DateTimeOffset RequestedAt,DateTimeOffset? ReceivedAt,string Status,
    string? RawDigest,Guid? EvidenceId,string SourceVersion,string ReceiptDigest);
public sealed record SourceImpact(string SourceId,string SourceVersion,string Reason,string[] ClaimKeys,string[] Components,string Action);
public sealed record DatasetBuildReport(string Component,string? DatasetSnapshotId,string[] SourceVersions,DateOnly CoverageStart,DateOnly CoverageEnd,
    int? FactsCount,int? RequestedObservations,int? EligibleObservations,int ApprovedResearchSamples,string? EligibilityDigest,Guid? ApprovalId,
    string? ApprovalDigest,string? SamplePolicyVersion,string[] BlockingReasons,string ReplayStatus,string ResearchStatus);
public sealed record Phase4JReport(string SchemaVersion,string Phase4IDigest,DateTimeOffset GeneratedAt,SourceCapability[] Sources,
    AcquisitionWork[] Plan,AcquisitionAttemptView[] Attempts,SourceImpact[] Impacts,DatasetBuildReport[] Builds,string Digest);

// Metadata over existing evidence and reviewer receipts, not an authority registry.
public static class Phase4JProjection
{
    public static string SourceId(string url)=>ResearchMath.Digest(new{OfficialUrl=url});
    public static string TaskId(string claim,string subject,string source,string version)=>ResearchMath.Digest(new{Claim=claim,Subject=subject,Source=source,Version=version});
    public static SourceCapability[] Sources(Phase4IReport r)=>r.Validation.Claims.SelectMany(c=>c.Sources.Select(s=>(Claim:c,Source:s)))
        .GroupBy(x=>(x.Source.SourceUrl,x.Source.RawDigest)).Select(g=>{
            var url=g.Key.SourceUrl;var shop=url.Contains("/products/",StringComparison.Ordinal);
            return new SourceCapability(SourceId(url),new Uri(url).Host,url,g.Key.RawDigest,"ARCHIVED_OFFICIAL_HTTP",
                [],g.Select(x=>Phase4IProjection.ClaimKey(x.Claim)).Distinct().Order(StringComparer.Ordinal).ToArray(),
                "UNKNOWN_HISTORICAL_APPLICABILITY","UNKNOWN","UNKNOWN","UNKNOWN_REUSE_RIGHTS",
                shop?"MANUAL_ACTION_REQUIRED":"PUBLIC_CAPTURE_REVIEW_REQUIRED",g.Max(x=>x.Source.RetrievedAt),g.Select(x=>x.Source.EvidenceId).Distinct().Order().ToArray());
        }).OrderBy(s=>s.SourceId,StringComparer.Ordinal).ThenBy(s=>s.SourceVersion,StringComparer.Ordinal).ToArray();
    public static AcquisitionWork[] Plan(Phase4IReport r,SourceCapability[] sources,AcquisitionAttemptView[] attempts)=>
        r.Validation.Claims.SelectMany(c=>sources.Where(s=>s.CandidateClaims.Contains(Phase4IProjection.ClaimKey(c))).Select(s=>{
            var key=Phase4IProjection.ClaimKey(c);var id=TaskId(key,c.SubjectDigest,s.SourceId,s.SourceVersion);
            var history=attempts.Where(a=>a.TaskId==id).OrderBy(a=>a.RequestedAt).ToArray();
            var manual=s.AccessStatus=="MANUAL_ACTION_REQUIRED";
            return new AcquisitionWork(id,key,c.SubjectDigest,c.Instruments,c.CoverageStart,c.CoverageEnd,c.Priority,s.SourceId,s.SourceVersion,
                "Original publication and retained revision evidence for "+key+"; exact subject/date/version scope",
                manual?"MANUAL_ACTION_REQUIRED":"CONTROLLED_CLI",manual?"MANUAL_ACTION_REQUIRED":history.LastOrDefault()?.Status??"PENDING",
                history.Length,c.BlockingReasons,manual?"Identify licensed archive and access terms; operator approval required before purchase/login/contact":
                "Review existing receipts first; acquire only this registered URL, then establish original availability and revision scope through existing reviewer CLI");
        })).DistinctBy(t=>t.TaskId).OrderBy(t=>t.Priority).ThenBy(t=>t.TaskId,StringComparer.Ordinal).ToArray();
    public static DatasetBuildReport[] Builds(Phase4IReport r,SourceImpact[] impacts)=>r.Validation.Components.Select(c=>{
        var trend=c.Evidence=="Trend";var p=r.Validation.Provenance;
        var invalid=impacts.Any(i=>i.Components.Contains(c.Evidence));
        return new DatasetBuildReport(c.Evidence,trend?p.DatasetSnapshotId:null,
            trend?r.Validation.Observations.SelectMany(o=>o.SourceVersions).Distinct().Order(StringComparer.Ordinal).ToArray():[],p.CoverageStart,p.CoverageEnd,
            trend?r.Counts.TotalFacts:null,trend?r.Counts.RequestedObservations:null,trend?r.Counts.EligibleObservations:null,
            trend&&!invalid?r.Counts.ApprovedResearchSamples:0,trend?p.EligibilityDigest:null,invalid?null:c.ApprovalId,invalid?null:c.ApprovalDigest,
            trend?r.Trend.Policy.Version:null,c.BlockingReasons.Concat(invalid?["SOURCE_VERSION_INVALIDATED_REEVALUATION_REQUIRED"]:Array.Empty<string>()).ToArray(),
            invalid?"REEVALUATION_REQUIRED":trend?r.Replay.EvidenceStatus:"NOT_BUILT_INDEPENDENT_APPROVAL_REQUIRED",invalid?"NOT_RUN":trend?r.Trend.Status:"NOT_RUN");
    }).ToArray();
    public static Phase4JReport Create(Phase4IReport r,AcquisitionAttemptView[] attempts,SourceImpact[] impacts,DateTimeOffset at)
    {
        Phase4IProjection.Validate(r);var sources=Sources(r);
        return Seal(new("Phase4J-v1",r.Digest,at,sources,Plan(r,sources,attempts),attempts,impacts,Builds(r,impacts),""));
    }
    public static Phase4JReport Seal(Phase4JReport r)=>r with{Digest=ResearchMath.Digest(r with{Digest=""})};
    public static void Validate(Phase4JReport r,Phase4IReport parent)
    {
        Phase4IProjection.Validate(parent);
        if(r.SchemaVersion!="Phase4J-v1"||r.Phase4IDigest!=parent.Digest||r.Digest!=ResearchMath.Digest(r with{Digest=""})||
            ResearchMath.Digest(r.Sources)!=ResearchMath.Digest(Sources(parent))||ResearchMath.Digest(r.Plan)!=ResearchMath.Digest(Plan(parent,r.Sources,r.Attempts))||
            ResearchMath.Digest(r.Builds)!=ResearchMath.Digest(Builds(parent,r.Impacts))||r.Attempts.Select(a=>a.AttemptId).Distinct().Count()!=r.Attempts.Length||
            r.Attempts.Any(a=>!r.Plan.Any(t=>t.TaskId==a.TaskId&&t.SourceVersion==a.SourceVersion)||a.ReceiptDigest.Length!=64||a.ReceivedAt<a.RequestedAt)||
            r.Impacts.Any(i=>!r.Sources.Any(s=>s.SourceId==i.SourceId&&s.SourceVersion==i.SourceVersion)||
                ResearchMath.Digest(i)!=ResearchMath.Digest(Invalidate(r,i.SourceId,i.SourceVersion,i.Reason).Single())))
            throw new InvalidDataException("Phase4J report/source/version binding mismatch");
    }
    public static SourceImpact[] Invalidate(Phase4JReport r,string sourceId,string version,string reason)
    {
        var s=r.Sources.Single(x=>x.SourceId==sourceId&&x.SourceVersion==version);
        if(reason is not ("REVOKED" or "SUPERSEDED" or "VERSION_MISMATCH"))throw new ArgumentException("Explicit invalidation reason required");
        return [new(s.SourceId,s.SourceVersion,reason,s.CandidateClaims,r.Builds.Select(b=>b.Component).ToArray(),"REEVALUATE_CURRENT_AUTHORITY_AND_APPROVAL")];
    }
}
