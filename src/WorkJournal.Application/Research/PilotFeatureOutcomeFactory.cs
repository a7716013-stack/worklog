using System.Collections.Immutable;
using WorkJournal.Domain.Research;
using WorkJournal.Web.Models.Evidence;
using WorkJournal.Web.Models.HistoricalResearch;
using WorkJournal.Web.Models.Research;
using WorkJournal.Web.Models.V2;
using WorkJournal.Web.Services.Evidence;
using WorkJournal.Web.Services.HistoricalResearch;
using WorkJournal.Web.Services.Research;
namespace WorkJournal.Application.Research;

public sealed record PilotFeatureOutcome(string SourceCodeSha,string ApprovalDigest,string InputDigest,string FormulaDigest,string OutcomeDigest,
    string Status,ResearchObservation[] Observations,SampleCell[] Samples,string[] BlockingReasons,string Oos,string Baseline,string Promotion,string Phase5,string Digest);
public static class PilotFeatureOutcomeFactory
{
    // Existing component approval is revalidated, not created. No baseline store or model promotion is invoked.
    public static PilotFeatureOutcome Prepare(ComponentDatasetStore approvals,Guid approvalId,PilotScope scope,PilotAuthorization authorization,PilotTrust trust,
        ResearchRequest template,string sha,DateTimeOffset now)
    {
        PilotScopeRules.Require(scope,authorization,trust,now,"RESEARCH","STORE");
        if(scope.Requests.Any(p=>p.Fixture)||scope.Requests.Any(p=>p.SourceCodeSha!=sha))throw new InvalidOperationException("REAL_APPROVED_SCOPE_REQUIRED");
        var archive=approvals.Current(approvalId);
        if(archive.Receipt.Component!=EvidenceCode.Trend||template.EvaluateOos||template.CandidateVersion!=V3CandidateCalculator.Version||
            V3CandidateCalculator.ParameterChecksum(template.Parameters)!=archive.Input.Closure.Dataset.Source.Dataset.ParameterChecksum)throw new InvalidOperationException("TREND_APPROVAL_SCOPE_MISMATCH");
        var d=archive.Input.Closure.Dataset.Source.Dataset;
        if(d.Archives.Any(a=>!scope.Requests.Any(p=>p.SourceUrl==a.SourceUri&&p.Provider==a.Provider&&p.Dataset==a.DatasetType.ToString()))||
            d.Facts.Any(f=>!scope.Instruments.Contains(f.SourceInstrumentKey)||f.TradeDate<scope.HistoricalStart||f.TradeDate>scope.HistoricalEnd)||
            template.Protocol.OutcomeHorizons.Any(h=>h!=scope.OutcomeHorizon)||scope.FeatureLookback<template.Parameters.MaLong+Math.Max(template.Parameters.SlopeHorizon??0,template.Parameters.StructureHorizon??0))throw new InvalidOperationException("FEATURE_OUTSIDE_APPROVED_PILOT");
        var build=new HistoricalDatasetBuilder().Build(d);
        if(build.DatasetSnapshotId!=archive.Receipt.DatasetSnapshotId||build.DatasetChecksum!=archive.Receipt.DatasetDigest)throw new InvalidDataException("APPROVED_BUILD_CHANGED");
        var outcomeCutoff=archive.Input.Closure.Dataset.Source.EvaluatedAt;
        if(outcomeCutoff<d.CreatedAt||outcomeCutoff>now)throw new InvalidDataException("OUTCOME_CUTOFF_OUTSIDE_ARCHIVE");
        var labelBuild=new HistoricalDatasetBuilder().Build(d with{CreatedAt=outcomeCutoff,Dates=[new(d.Calendar.Sessions.Max(),outcomeCutoff)]});
        var series=labelBuild.Decisions.Where(x=>x.Eligible&&x.InstrumentId is not null).GroupBy(x=>x.InstrumentId!.Value).Select(g=>
            new OutcomeSeries(g.Key,d.PricePolicy.PriceBasisVersion,g.Select(x=>(Decision:x,Fact:d.Facts.Single(f=>f.FactChecksum==x.FactChecksum)))
                .Where(x=>x.Fact.DatasetType==HistoricalDatasetType.Price&&x.Fact.Value.Close is not null)
                .Select(x=>new OutcomeClose(x.Fact.TradeDate,x.Fact.Value.Close!.Value,x.Decision.ResolvedAvailableAt,x.Fact.QualityStatus)).OrderBy(x=>x.Date).ToImmutableArray())).OrderBy(x=>x.InstrumentId).ToImmutableArray();
        var outcomes=new OutcomeDataset(labelBuild.DatasetSnapshotId,"approved-facts-v1",outcomeCutoff,d.Calendar.Sessions,series);
        return Build(build,template with{Snapshots=build.Snapshots,Outcomes=outcomes},sha,archive.Receipt.ApprovalDigest);
    }
    // Pure deterministic factory also used by isolated fixture tests; it cannot mint an approved dataset.
    public static PilotFeatureOutcome Build(HistoricalBuildResult build,ResearchRequest request,string sha,string approvalDigest)
    {
        if(sha.Length!=40||!sha.All(Uri.IsHexDigit)||!PilotScopeRules.Digest(approvalDigest)||request.CandidateVersion!=V3CandidateCalculator.Version||request.EvaluateOos||
            ResearchMath.Digest(request.Snapshots)!=ResearchMath.Digest(build.Snapshots)||request.Outcomes.Series.Select(x=>x.InstrumentId).Distinct().Count()!=request.Outcomes.Series.Length)
            throw new InvalidDataException("FEATURE_OUTCOME_BINDING_INVALID");
        ResearchProtocolRules.Validate(request.Protocol,request.Outcomes.Sessions);
        bool In(DateOnly date,ResearchPeriod period)=>date>=period.Start&&date<=period.End;
        var observations=new List<ResearchObservation>();var cells=new List<SampleCell>();
        foreach(var snapshot in build.Snapshots.OrderBy(x=>x.Data.Identity.TradeDate))
        {
            var date=snapshot.Data.Identity.TradeDate;
            if(!request.Protocol.Folds.Any(f=>In(date,f.Train)||In(date,f.Validation)))continue;
            if(snapshot.Data.Identity.DataCutoff>request.Outcomes.EvaluationCutoff)throw new InvalidDataException("FEATURE_CUTOFF_AFTER_OUTCOME");
            foreach(var id in snapshot.Data.UniverseMembers.Order())
            {
                var trace=new V3CandidateCalculator().Calculate(snapshot.Data,id,request.Parameters);
                var anchors=snapshot.Data.Series.Where(s=>s.InstrumentId==id).SelectMany(s=>s.Bars).Where(b=>b.Date==date&&b.AvailableAt<=snapshot.Data.Identity.DataCutoff&&b.Status==EvidenceStatus.Available).ToArray();
                var labels=request.Protocol.OutcomeHorizons.Select(h=>{
                    var result=ResearchOutcomeCalculator.Calculate(trace,h,request.Outcomes,anchors.Length==1?anchors[0].Close:null);
                    return result.LabelEnd is {} end&&request.Protocol.Folds.Any(f=>new[]{f.Train,f.Validation}.Any(p=>In(date,p)&&end<=p.End))?result:
                        result with{FutureReturn=null,FutureMaxCloseReturn=null,FutureMinCloseReturn=null,Status="PURGED_PARTITION_BOUNDARY"};
                }).ToImmutableArray();
                observations.Add(new(request.ResearchRunId,trace,labels));
            }
        }
        foreach(var fold in request.Protocol.Folds)foreach(var part in new[]{ResearchPartition.Train,ResearchPartition.Validation})foreach(var horizon in request.Protocol.OutcomeHorizons)
        {
            var period=ResearchProtocolRules.Period(fold,part);var rows=observations.Where(x=>In(x.Trace.Identity.TradeDate,period)).ToArray();
            var values=rows.Select(o=>new ResearchValue(o.Trace.Identity.TradeDate,o.Trace.InstrumentId,ResearchEngine.Value(o.Trace,"Trend"),
                o.Outcomes.Single(x=>x.Horizon==horizon).LabelEnd<=period.End?o.Outcomes.Single(x=>x.Horizon==horizon).FutureReturn:null,null)).ToArray();
            var metric=ResearchMetrics.Calculate(fold.Code,part,"Trend",horizon,values,request.Protocol.Quantiles,rows.Count(o=>o.Outcomes.Single(x=>x.Horizon==horizon).LabelEnd>period.End));
            var policy=SamplePolicy.Phase4I;var cross=Math.Max(policy.MinimumInstrumentsPerDate,request.Protocol.Quantiles);
            var dates=metric.DailyIc.Count(x=>x.Count>=cross&&x.PearsonIc is not null&&x.RankIc is not null);var reasons=new List<string>();
            if(dates<policy.MinimumDates)reasons.Add("INSUFFICIENT_DATES_OR_CROSS_SECTION_OR_VARIANCE");
            if(metric.OutcomePairCount<policy.MinimumOutcomePairs)reasons.Add("INSUFFICIENT_OUTCOME_PAIRS");
            if(metric.Quantiles.Length!=request.Protocol.Quantiles||metric.Quantiles.Any(x=>x.Count==0))reasons.Add("INSUFFICIENT_QUANTILE_COVERAGE");
            cells.Add(new(fold.Code,part.ToString(),horizon,metric.OutcomePairCount,dates,cross,reasons.Count==0,reasons.ToArray()));
        }
        var result=new PilotFeatureOutcome(sha,approvalDigest,ResearchMath.Digest(new{build.InputDigest,Request=ResearchEngine.CanonicalRequest(request)}),FormulaCatalog.Identity.FormulaDigest,ResearchMath.Digest(request.Outcomes),
            cells.Count>0&&cells.All(x=>x.Sufficient)?"PREPARED_REQUIRES_SEPARATE_BASELINE_AUTHORIZATION":"INSUFFICIENT_SAMPLE",observations.ToArray(),cells.ToArray(),
            cells.SelectMany(x=>x.Reasons).Distinct().ToArray(),"NOT_RUN_SEPARATE_SELECTION_REQUIRED","NOT_RUN","NOT_PROMOTED","LOCKED","");
        return result with{Digest=ResearchMath.Digest(result)};
    }
}
