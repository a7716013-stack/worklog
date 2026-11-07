using WorkJournal.Web.Models.HistoricalResearch.Resolution;
using WorkJournal.Web.Services.HistoricalResearch;
namespace WorkJournal.Application.Research;

public sealed class Phase4JWorkflow(string root,string evidenceRoot,string authorityRoot,string closureRoot)
{
    public Phase4JReport Export(TrendResolutionRequest request,string sha,DateTimeOffset at,Guid? approvalId=null)
    {
        // Reevaluate existing current authority/approval and replay raw archives before preparing metadata.
        var parent=new Phase4IWorkflow(root,evidenceRoot,authorityRoot,closureRoot).Export(request,sha,at,approvalId);
        return Project(parent,request,at);
    }
    public Phase4JReport Project(Phase4IReport parent,TrendResolutionRequest request,DateTimeOffset at)
    {
        var current=new AuthorityResolutionStore(root,evidenceRoot,authorityRoot,closureRoot).Evaluate(request);
        if(current.Digest!=parent.Validation.SourceReportDigest)throw new InvalidDataException("Authority changed; re-export current evaluation");
        var sources=Phase4JProjection.Sources(parent);var ledger=new AuthorityResolutionLedger(root,evidenceRoot);
        var views=new List<AcquisitionAttemptView>();
        foreach(var legacy in current.Queue)
        {
            var key=legacy.AuthorityDomain+":"+legacy.RequiredClaimId;
            var claim=parent.Validation.Claims.Single(c=>Phase4IProjection.ClaimKey(c)==key);
            foreach(var a in ledger.Attempts(legacy.ResolutionTaskId))
            {
                var source=sources.SingleOrDefault(s=>s.OfficialUrl==a.SourceReference&&s.SourceVersion==a.RawDigest);
                // Failed legacy attempts have no version binding. Keep them in the original ledger, not a guessed source version.
                if(source is null||!source.CandidateClaims.Contains(key))continue;
                views.Add(new(a.AttemptId,Phase4JProjection.TaskId(key,claim.SubjectDigest,source.SourceId,source.SourceVersion),a.RequestedAt,a.ReceivedAt,
                    AcquisitionSafety.Classify(a.AcquisitionStatus),a.RawDigest,a.EvidenceId,source.SourceVersion,a.Digest));
            }
        }
        var dir=Path.Combine(root,"supply-chain");
        if(Directory.Exists(dir))foreach(var file in Directory.GetFiles(dir,"*.result.json"))
        {
            var view=HistoricalDatasetStore.Read<AcquisitionAttemptView>(file);
            var task=Phase4JProjection.Plan(parent,sources,[]).Single(t=>t.TaskId==view.TaskId);
            var legacy=current.Queue.Single(t=>t.AuthorityDomain+":"+t.RequiredClaimId==task.ClaimKey);
            var receipt=ledger.Attempts(legacy.ResolutionTaskId).Single(a=>a.AttemptId==view.AttemptId);
            if(receipt.Digest!=view.ReceiptDigest||receipt.RawDigest!=view.RawDigest||receipt.EvidenceId!=view.EvidenceId||
                AcquisitionSafety.Classify(receipt.AcquisitionStatus)!=view.Status||receipt.RequestedAt!=view.RequestedAt||receipt.ReceivedAt!=view.ReceivedAt)
                throw new InvalidDataException("Supply-chain attempt changed");
            views.RemoveAll(a=>a.AttemptId==view.AttemptId);views.Add(view);
        }
        var seed=Phase4JProjection.Create(parent,views.OrderBy(a=>a.RequestedAt).ThenBy(a=>a.AttemptId).ToArray(),[],at);
        var impacts=new List<SourceImpact>();
        if(Directory.Exists(dir))foreach(var file in Directory.GetFiles(dir,"*.impact.json"))
        {
            var impact=HistoricalDatasetStore.Read<SourceImpact>(file);
            var expected=Phase4JProjection.Invalidate(seed,impact.SourceId,impact.SourceVersion,impact.Reason).Single();
            if(WorkJournal.Web.Services.Research.ResearchMath.Digest(impact)!=WorkJournal.Web.Services.Research.ResearchMath.Digest(expected))throw new InvalidDataException("Invalidation scope changed");
            impacts.Add(impact);
        }
        foreach(var a in views.Where(a=>a.RawDigest is not null&&a.RawDigest!=a.SourceVersion))
        {var task=seed.Plan.Single(t=>t.TaskId==a.TaskId);impacts.AddRange(Phase4JProjection.Invalidate(seed,task.SourceId,task.SourceVersion,"VERSION_MISMATCH"));}
        var report=Phase4JProjection.Create(parent,seed.Attempts,impacts.DistinctBy(i=>(i.SourceId,i.SourceVersion,i.Reason)).OrderBy(i=>i.SourceId,StringComparer.Ordinal).ThenBy(i=>i.Reason,StringComparer.Ordinal).ToArray(),at);
        Phase4JProjection.Validate(report,parent);return report;
    }
}
