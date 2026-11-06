using WorkJournal.Web.Models.HistoricalResearch.Resolution;
using WorkJournal.Web.Models.Research;
using WorkJournal.Web.Services.HistoricalResearch;
using WorkJournal.Web.Services.Research;
namespace WorkJournal.Application.Research;
public sealed class Phase4IWorkflow(string root,string evidenceRoot,string authorityRoot,string closureRoot)
{
    public Phase4IReport Export(TrendResolutionRequest request,string sha,DateTimeOffset at,Guid? approvalId=null)
    {
        var v=new EvidenceValidationWorkflow(root,evidenceRoot,authorityRoot,closureRoot).Evaluate(request,sha,at,approvalId);
        var evidence=new EvidenceAcquisitionStore(evidenceRoot);
        var decisions=v.Claims.SelectMany(c=>c.Sources.SelectMany(s=>s.ReviewDigests.Select(d=>
        {
            var review=evidence.ReadReview(d);
            return new ReviewerDecision(Phase4IProjection.ClaimKey(c),s.EvidenceId,s.RawDigest,review.ReviewDigest,review.Reviewer,review.ReviewedAt,
                review.EffectiveFrom,review.EffectiveTo,review.Ambiguous.Length>0?"UNPROVEN":"REVIEWED_SCOPE_ONLY",review.Supported,review.Ambiguous.Concat(review.Unsupported).Distinct().ToArray(),review.Notes);
        }))).OrderBy(x=>x.ClaimKey,StringComparer.Ordinal).ThenBy(x=>x.ReviewDigest,StringComparer.Ordinal).ToArray();
        var data=request.Input.Closure.Dataset.Source.Dataset;
        var facts=v.Dataset.RawRealRows==0?[]:data.Facts.Select(f=>new FactCountIdentity(f.FactChecksum,f.Provider,f.DatasetType.ToString(),f.SourceInstrumentKey,f.TradeDate,f.RevisionId)).ToArray();
        return Phase4IProjection.Seal(new("Phase4I-v1",v,facts,Phase4IProjection.Count(facts,v),decisions,Phase4IProjection.Pending(v),
            new("VERIFIED_AT_EXPORT","NOT_RUN",at,v.Digest,null,"Evidence/approval replay at controlled export; HTTP checks published report integrity, not live archives"),""));
    }
    public Phase4IReport Run(TrendResolutionRequest request,Guid approvalId,ResearchRequest template,string sha,DateTimeOffset at)
    {
        var report=Export(request,sha,at,approvalId);
        if(report.Validation.Dataset.Status!=ResearchEligibilityStatus.READY_FOR_CONTROLLED_BASELINE)throw new InvalidOperationException("BLOCKED_BY_AUTHORITY_OR_DATA");
        var store=new AuthorityResolutionStore(root,evidenceRoot,authorityRoot,closureRoot);
        var preview=store.PreviewBaseline(approvalId,template,sha,at);
        var assessment=Phase4IProjection.Assess(preview,template.Protocol,SamplePolicy.Phase4I);
        if(assessment.Status!="COMPLETED")return Phase4IProjection.Seal(report with{Trend=assessment});
        store.Baseline(approvalId,template,sha);store.BaselineReplay(template.ResearchRunId);
        var result=new RealBaselineStore(Path.Combine(root,"components","baselines")).Replay(template.ResearchRunId);
        var summary=Phase4IProjection.Assess(result,template.Protocol,SamplePolicy.Phase4I);
        return Phase4IProjection.Seal(report with{Trend=summary,Replay=report.Replay with{BaselineStatus="VERIFIED_AT_EXPORT",BaselineDigest=result.Run.OutputDigest}});
    }
}
