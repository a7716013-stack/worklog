using WorkJournal.Web.Models.HistoricalResearch;
using WorkJournal.Web.Models.Research;
using WorkJournal.Web.Services.HistoricalResearch;
using WorkJournal.Web.Services.Research;
namespace WorkJournal.Application.Research;

public sealed record FactCountIdentity(string FactChecksum,string Provider,string DatasetType,string Instrument,DateOnly TradeDate,string RevisionId);
public sealed record ResearchCounts(int TotalFacts,int PriceFacts,int InstitutionalFacts,int BenchmarkFacts,int UniqueFacts,int DuplicateFacts,
    int UniqueTypeInstrumentDates,int AdditionalRevisions,int RequestedObservations,int EligibleObservations,int ApprovedResearchSamples,string Definition);
public sealed record ReviewerDecision(string ClaimKey,Guid EvidenceId,string RawDigest,string ReviewDigest,string Reviewer,DateTimeOffset ReviewedAt,
    DateOnly CoverageStart,DateOnly CoverageEnd,string Decision,string[] Supported,string[] Unproven,string Notes);
public sealed record ClaimRequirement(string Id,ClaimClosureAudit Claim,string RequiredProof,string Applicability,ReviewerDecision[] Decisions);
public sealed record SamplePolicy(string Version,int MinimumDates,int MinimumInstrumentsPerDate,int MinimumOutcomePairs)
{
    // Research eligibility policy, not Candidate-v1 scoring parameters or a significance claim.
    public static SamplePolicy Phase4I => new("TrendSample-v1",20,10,200);
    public void Validate(){if(string.IsNullOrWhiteSpace(Version)||MinimumDates<2||MinimumInstrumentsPerDate<2||MinimumOutcomePairs<2)throw new ArgumentException("Explicit prospective sample policy required");}
}
public sealed record SampleCell(string Fold,string Partition,int Horizon,int OutcomePairs,int UsableDates,int RequiredCrossSection,bool Sufficient,string[] Reasons);
public sealed record TrendValidationSummary(string RunId,string Status,string Component,SamplePolicy Policy,SampleCell[] Samples,
    ResearchMetric[]? Metrics,BaselineIcSummary[]? IC,ResearchCorrelation[]? Correlations,object[]? Stability,
    string OosStatus,string? OutputDigest,string[] BlockingReasons);
public sealed record EvidenceReplayStatus(string EvidenceStatus,string BaselineStatus,DateTimeOffset VerifiedAt,string EvidenceDigest,string? BaselineDigest,string Semantics);
public sealed record Phase4IReport(string SchemaVersion,EvidenceValidationReport Validation,FactCountIdentity[] Facts,ResearchCounts Counts,
    ReviewerDecision[] Decisions,TrendValidationSummary Trend,EvidenceReplayStatus Replay,string Digest);

public static class Phase4IProjection
{
    public static string ClaimKey(ClaimClosureAudit c)=>c.Domain+":"+c.ClaimId;
    public static ResearchCounts Count(FactCountIdentity[] facts,EvidenceValidationReport v)
    {
        var unique=facts.DistinctBy(f=>f.FactChecksum).ToArray();
        var keys=unique.Select(f=>(f.Provider,f.DatasetType,f.Instrument,f.TradeDate)).Distinct().Count();
        return new(facts.Length,facts.Count(f=>f.DatasetType=="Price"),facts.Count(f=>f.DatasetType=="Institutional"),facts.Count(f=>f.DatasetType=="Benchmark"),
            unique.Length,facts.Length-unique.Length,keys,unique.Length-keys,v.Observations.Length,v.Observations.Count(o=>o.Eligible),
            v.Dataset.Status==ResearchEligibilityStatus.READY_FOR_CONTROLLED_BASELINE?v.Observations.Count(o=>o.Eligible):0,
            "Parsed source facts; type/provider/instrument/date keys. Revisions are not duplicate checksums. Requested observations are instrument/decision-date pairs; samples require approval. RawRealRows is the legacy total-facts alias, not CSV line count.");
    }
    public static ClaimRequirement[] Queue(Phase4IReport r)=>r.Validation.Claims.Select(c=>new ClaimRequirement(ClaimKey(c),c,
        "Scoped historical identity, coverage, original publication and revision evidence; capture time/checksum alone is insufficient",
        c.CurrentStatus=="SUPPORTED"&&c.BlockingReasons.Length==0?"SUPPORTED_FOR_REVIEWED_SCOPE":"UNPROVEN_FOR_HISTORICAL_SCOPE",
        r.Decisions.Where(d=>d.ClaimKey==ClaimKey(c)).ToArray())).OrderBy(c=>c.Claim.Priority).ThenBy(c=>c.Id,StringComparer.Ordinal).ToArray();
    public static TrendValidationSummary Pending(EvidenceValidationReport r)=>new(r.Provenance.RunId,"NOT_RUN","Trend",SamplePolicy.Phase4I,[],null,null,null,null,
        "NOT_RUN_SEPARATE_SELECTION_REQUIRED",null,r.Dataset.BlockingReasons.Length>0?r.Dataset.BlockingReasons:["CONTROLLED_SAMPLE_ASSESSMENT_REQUIRED"]);
    public static TrendValidationSummary Assess(BaselineResearchReport report,ResearchProtocol protocol,SamplePolicy policy)
    {
        policy.Validate();
        var cells=new List<SampleCell>();
        foreach(var fold in protocol.Folds)foreach(var part in new[]{ResearchPartition.Train,ResearchPartition.Validation})foreach(var horizon in protocol.OutcomeHorizons)
        {
            var metric=report.Metrics.SingleOrDefault(m=>m.Variable=="Trend"&&m.Fold==fold.Code&&m.Partition==part&&m.Horizon==horizon);
            var cross=Math.Max(policy.MinimumInstrumentsPerDate,protocol.Quantiles);
            var dates=metric?.DailyIc.Where(d=>d.Count>=cross&&d.PearsonIc.HasValue&&d.RankIc.HasValue).Select(d=>d.Date).Distinct().Count()??0;
            var reasons=new List<string>();
            if(dates<policy.MinimumDates)reasons.Add("INSUFFICIENT_DATES_OR_CROSS_SECTION_OR_VARIANCE");
            if((metric?.OutcomePairCount??0)<policy.MinimumOutcomePairs)reasons.Add("INSUFFICIENT_OUTCOME_PAIRS");
            if(metric is null||metric.Quantiles.Length!=protocol.Quantiles||metric.Quantiles.Any(q=>q.Count==0))reasons.Add("INSUFFICIENT_QUANTILE_COVERAGE");
            cells.Add(new(fold.Code,part.ToString(),horizon,metric?.OutcomePairCount??0,dates,cross,reasons.Count==0,reasons.ToArray()));
        }
        var sufficient=cells.Count>0&&cells.All(c=>c.Sufficient)&&report.Run.Status=="BASELINE_COMPLETED";
        var blockers=cells.SelectMany(c=>c.Reasons).Concat(report.Run.Status=="BASELINE_COMPLETED"?[]:new[]{"INCOMPLETE_APPROVED_COMPONENT_FEATURES_OR_OUTCOMES"}).Distinct().ToArray();
        if(cells.Count==0)blockers=["EMPTY_PROTOCOL_SCOPE"];
        return new(report.Run.ResearchRunId.ToString(),sufficient?"COMPLETED":"INSUFFICIENT_SAMPLE","Trend",policy,cells.ToArray(),
            sufficient?report.Metrics.Where(m=>m.Variable=="Trend").ToArray():null,sufficient?report.IC.Where(m=>m.Variable=="Trend").ToArray():null,
            sufficient?report.Correlations:null,sufficient?report.TimeStability:null,"NOT_RUN_SEPARATE_SELECTION_REQUIRED",sufficient?report.Run.OutputDigest:null,blockers);
    }
    public static Phase4IReport Seal(Phase4IReport r)=>r with{Digest=ResearchMath.Digest(r with{Digest=""})};
    public static void Validate(Phase4IReport r)
    {
        if(r is null||r.Validation is null||r.Facts is null||r.Counts is null||r.Decisions is null||r.Trend is null||r.Replay is null)throw new InvalidDataException("Incomplete Phase4I report");
        EvidenceValidationWorkflow.Validate(r.Validation);r.Trend.Policy.Validate();
        if(r.SchemaVersion!="Phase4I-v1"||r.Digest!=ResearchMath.Digest(r with{Digest=""})||
            ResearchMath.Digest(r.Counts)!=ResearchMath.Digest(Count(r.Facts,r.Validation))||r.Counts.TotalFacts!=r.Validation.Dataset.RawRealRows||
            r.Replay.EvidenceDigest!=r.Validation.Digest||r.Replay.EvidenceStatus!="VERIFIED_AT_EXPORT"||
            r.Decisions.Any(d=>!r.Validation.Claims.Any(c=>ClaimKey(c)==d.ClaimKey&&c.Sources.Any(s=>s.EvidenceId==d.EvidenceId&&s.RawDigest==d.RawDigest&&s.ReviewDigests.Contains(d.ReviewDigest)))||string.IsNullOrWhiteSpace(d.Reviewer)||d.Decision is not ("UNPROVEN" or "REVIEWED_SCOPE_ONLY"))||
            r.Trend.Status is not ("NOT_RUN" or "INSUFFICIENT_SAMPLE" or "COMPLETED")||
            r.Trend.Status=="INSUFFICIENT_SAMPLE"&&r.Validation.Provenance.DatasetApprovalId is null||
            r.Trend.Status!="COMPLETED"&&(r.Trend.Metrics is not null||r.Trend.IC is not null||r.Trend.Correlations is not null||r.Trend.Stability is not null||r.Trend.OutputDigest is not null)||
            r.Trend.Status=="COMPLETED"&&(r.Validation.Provenance.DatasetApprovalId is null||r.Trend.Samples.Length==0||r.Trend.Samples.Any(c=>!c.Sufficient||c.UsableDates<r.Trend.Policy.MinimumDates||c.OutcomePairs<r.Trend.Policy.MinimumOutcomePairs||c.RequiredCrossSection<r.Trend.Policy.MinimumInstrumentsPerDate)||r.Trend.Metrics is null||r.Trend.Metrics.Any(m=>m.Partition==ResearchPartition.Oos||m.Variable!="Trend")||r.Trend.OutputDigest is null||r.Replay.BaselineStatus!="VERIFIED_AT_EXPORT"||r.Replay.BaselineDigest!=r.Trend.OutputDigest))
            throw new InvalidDataException("Phase4I report identity, counts or research state mismatch");
    }
}
public sealed class Phase4IReadService(string path)
{
    public Phase4IReport Read(){var r=HistoricalDatasetStore.Read<Phase4IReport>(path);Phase4IProjection.Validate(r);return r;}
}
