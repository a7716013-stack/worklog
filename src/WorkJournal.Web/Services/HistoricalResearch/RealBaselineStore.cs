using System.Collections.Immutable;
using WorkJournal.Web.Models.Evidence;
using WorkJournal.Web.Models.HistoricalResearch;
using WorkJournal.Web.Models.Research;
using WorkJournal.Web.Models.V2;
using WorkJournal.Web.Services.Evidence;
using WorkJournal.Web.Services.Research;
namespace WorkJournal.Web.Services.HistoricalResearch;

public sealed record BaselineDiagnostic(string Component,string Feature,int Count,int Missing,int Invalid,int Stale,
    ResearchDistribution Distribution,decimal? PercentAt0,decimal? PercentAt1);
public sealed record BaselineIcSummary(string Fold,string Variable,int Horizon,ResearchPartition Partition,int ObservationDates,
    decimal? MeanIC,decimal? MedianIC,decimal? StdDevIC,decimal? PositiveICRatio,
    decimal? MeanRankIC,decimal? MedianRankIC,decimal? StdDevRankIC,decimal? PositiveRankICRatio);
public sealed record BaselineEvidenceRow(Guid InstrumentId,DateOnly Date,string CalculationId,string InputDigest,
    CandidateComponent[] Components,decimal? FullEvidence100);
public sealed record RealBaselineRun(Guid ResearchRunId,string CandidateVersion,Guid ParameterSetId,string ParameterChecksum,
    string DatasetApprovalId,string ResearchProtocolVersion,DateOnly CoverageStart,DateOnly CoverageEnd,
    string InputDigest,string OutputDigest,DateTimeOffset StartedAt,DateTimeOffset CompletedAt,string Status);
public sealed record BaselineResearchReport(RealBaselineRun Run,BaselineEvidenceRow[] Evidence,
    BaselineDiagnostic[] Diagnostics,ResearchMetric[] Metrics,BaselineIcSummary[] IC,
    ResearchCorrelation[] Correlations,object[] TimeStability,string[] Findings,
    string V3PromotionStatus="NOT_PROMOTED",string V3ThresholdStatus="UNRESOLVED",string Phase5Status="LOCKED");
public sealed record ResearchHypothesis(Guid HypothesisId,string ObservedBaselineFinding,string ProposedChange,
    string ExpectedEffect,string AffectedEvidence,string AffectedFeature,DateTimeOffset CreatedAt);
public sealed record BaselineArchive(RealDatasetSubmission Submission,FirstApprovedRealPitDatasetReport Approval,
    ResearchRequest Request,DateTimeOffset CompletedAt);

public sealed class RealBaselineStore(string root)
{
    public string Execute(RealDatasetSubmission submission,ResearchRequest template,string evidenceRoot,string authorityRoot,TimeProvider clock)
    {
        var approval=new RealDataApproval(evidenceRoot,authorityRoot).Evaluate(submission);
        if(approval.ComponentApprovals.Length==0)throw new InvalidOperationException("BLOCKED_BY_DATA: reviewed component dataset required.");
        if(template.Snapshots.Length!=0||template.EvaluateOos)throw new ArgumentException("Empty snapshot template and development-only protocol required.");
        var dataset=new HistoricalDatasetBuilder().Build(submission.Source.Dataset);
        var request=template with{Snapshots=dataset.Snapshots};ValidateLabels(submission,request);
        var dir=Path.Combine(root,request.ResearchRunId.ToString("N"));Directory.CreateDirectory(dir);
        // Reserve the run and freeze approved features before outcome calculation.
        HistoricalDatasetStore.WriteNew(Path.Combine(dir,"request.json"),request);
        var rows=Freeze(request,approval);
        HistoricalDatasetStore.WriteNew(Path.Combine(dir,"frozen-evidence.json"),rows);
        var completed=clock.GetUtcNow();
        if(completed<request.CreatedAt)throw new InvalidOperationException("Completion cannot precede run start.");
        var archive=new BaselineArchive(submission,approval,request,completed);
        var report=Calculate(archive,rows);
        HistoricalDatasetStore.WriteNew(Path.Combine(dir,"archive.json"),archive);
        HistoricalDatasetStore.WriteNew(Path.Combine(dir,"baseline-report.json"),report);
        var files=new[]{"request.json","frozen-evidence.json","archive.json","baseline-report.json"};
        HistoricalDatasetStore.WriteNew(Path.Combine(dir,"manifest.json"),files.ToDictionary(f=>f,f=>ResearchMath.Digest(File.ReadAllText(Path.Combine(dir,f)))));
        return dir;
    }
    public BaselineResearchReport Replay(Guid id)
    {
        var dir=Path.Combine(root,id.ToString("N"));var files=new[]{"request.json","frozen-evidence.json","archive.json","baseline-report.json"};
        var manifest=HistoricalDatasetStore.Read<Dictionary<string,string>>(Path.Combine(dir,"manifest.json"));
        if(manifest.Count!=files.Length||files.Any(f=>!manifest.TryGetValue(f,out var hash)||hash!=ResearchMath.Digest(File.ReadAllText(Path.Combine(dir,f)))))throw new InvalidDataException("Baseline archive changed.");
        var a=HistoricalDatasetStore.Read<BaselineArchive>(Path.Combine(dir,"archive.json"));
        if(a.Request.ResearchRunId!=id||a.Approval.InputDigest!=RealDataApproval.InputDigest(a.Submission)||
            a.Approval.ApprovalDigest!=ResearchMath.Digest(a.Approval with{ApprovalDigest=""})||a.Approval.ComponentApprovals.Length==0)
            throw new InvalidDataException("Baseline approval scope mismatch.");
        ValidateLabels(a.Submission,a.Request);
        var frozen=Freeze(a.Request,a.Approval);
        if(ResearchMath.Digest(frozen)!=ResearchMath.Digest(HistoricalDatasetStore.Read<BaselineEvidenceRow[]>(Path.Combine(dir,"frozen-evidence.json"))))throw new InvalidDataException("Frozen evidence replay mismatch.");
        var report=Calculate(a,frozen);
        if(ResearchMath.Digest(report)!=ResearchMath.Digest(HistoricalDatasetStore.Read<BaselineResearchReport>(Path.Combine(dir,"baseline-report.json"))))throw new InvalidDataException("Baseline replay mismatch.");
        return report;
    }
    private static void ValidateLabels(RealDatasetSubmission s,ResearchRequest r)
    {
        if(r.EvaluateOos||r.CandidateVersion!=V3CandidateCalculator.Version||r.CreatedAt<s.Review?.ReviewedAt||
            r.Outcomes.EvaluationCutoff>s.Source.EvaluatedAt||!r.Outcomes.Sessions.SequenceEqual(s.Source.Dataset.Calendar.Sessions))
            throw new InvalidOperationException("Baseline protocol, approval time or outcome calendar mismatch.");
        var data=new HistoricalDatasetBuilder().Build(s.Source.Dataset);
        if(ResearchMath.Digest(data.Snapshots)!=ResearchMath.Digest(r.Snapshots))throw new InvalidOperationException("Snapshot scope mismatch.");
        var labelDataset=s.Source.Dataset with{Dates=[new(r.Outcomes.Sessions.Max(),r.Outcomes.EvaluationCutoff)]};
        var labelEligible=new HistoricalDatasetBuilder().Build(labelDataset).Decisions.Where(x=>x.Eligible).Select(x=>x.FactChecksum).ToHashSet();
        foreach(var series in r.Outcomes.Series)foreach(var close in series.Closes)
        {
            var matching=s.Source.Dataset.Facts.Where(f=>labelEligible.Contains(f.FactChecksum)&&f.DatasetType==HistoricalDatasetType.Price&&f.TradeDate==close.Date&&
                f.PriceBasisVersion==series.PriceBasisVersion&&f.Value.Close==close.Close&&
                s.Source.Dataset.Instruments.Count(i=>i.InstrumentId==series.InstrumentId&&i.Provider==f.Provider&&i.SourceInstrumentKey==f.SourceInstrumentKey&&
                    i.ValidFrom<=f.TradeDate&&(!i.ValidTo.HasValue||i.ValidTo>=f.TradeDate)&&i.AvailableAt<=r.Outcomes.EvaluationCutoff)==1&&
                HistoricalAvailability.Resolve(f,s.Source.Dataset.Policies.SingleOrDefault(p=>p.PolicyId==f.AvailabilityPolicyId)).At==close.AvailableAt&&
                HistoricalAuthority.Authorize(s.Source,f).Authorized).ToArray();
            if(matching.Length!=1||close.Status!=EvidenceStatus.Available||close.AvailableAt is null||close.AvailableAt>r.Outcomes.EvaluationCutoff)
                throw new InvalidOperationException("Outcome needs unique approved source fact and compatible price basis.");
        }
    }
    private static bool DevelopmentDate(ResearchRequest r,DateOnly date)=>r.Protocol.Folds.Any(f=>
        (date>=f.Train.Start&&date<=f.Train.End)||(date>=f.Validation.Start&&date<=f.Validation.End));
    private static BaselineEvidenceRow[] Freeze(ResearchRequest r,FirstApprovedRealPitDatasetReport approval)
    {
        var codes=approval.ComponentApprovals.Select(c=>c.EvidenceCode).ToHashSet();
        return r.Snapshots.Where(s=>DevelopmentDate(r,s.Data.Identity.TradeDate)).OrderBy(s=>s.Data.Identity.TradeDate)
            .SelectMany(s=>s.Data.UniverseMembers.Order().Select(id=>
            {
                var t=new V3CandidateCalculator().Calculate(s.Data,id,r.Parameters);
                return new BaselineEvidenceRow(id,t.Identity.TradeDate,t.CalculationId,t.InputDigest,
                    t.Components.Where(c=>codes.Contains(c.Evidence.Code)).ToArray(),approval.FullApproval is null?null:t.Score.TotalScore);
            })).ToArray();
    }
    private static BaselineResearchReport Calculate(BaselineArchive a,BaselineEvidenceRow[] frozen)
    {
        var r=a.Request;var result=new ResearchEngine().Run(r);var codes=a.Approval.ComponentApprovals.Select(c=>c.EvidenceCode.ToString()).ToHashSet();
        var full=a.Approval.FullApproval is not null;
        var metrics=result.Metrics.Where(m=>codes.Contains(m.Variable)||(full&&(m.Variable=="FullEvidence100"||m.Variable.StartsWith("Ablation.")))).ToArray();
        var diagnostics=new List<BaselineDiagnostic>();
        foreach(var code in codes.Order(StringComparer.Ordinal))
        {
            var components=frozen.SelectMany(x=>x.Components).Where(c=>c.Evidence.Code.ToString()==code).ToArray();
            var values=components.Select(c=>c.Evidence.Value).ToArray();var valid=values.Where(v=>v.HasValue).Select(v=>v!.Value).ToArray();
            diagnostics.Add(new(code,"Evidence",valid.Length,components.Count(c=>c.Evidence.Status==EvidenceStatus.Missing),
                components.Count(c=>c.Evidence.Status==EvidenceStatus.Invalid),components.Count(c=>c.Evidence.Status==EvidenceStatus.Stale),
                ResearchMath.Distribution(values),valid.Length==0?null:(decimal)valid.Count(v=>v==0)/valid.Length,valid.Length==0?null:(decimal)valid.Count(v=>v==1)/valid.Length));
            foreach(var feature in CandidateFeatureCatalog.Create(Guid.Empty).Where(f=>f.Domain.ToString()==code).Select(f=>f.Code).Order(StringComparer.Ordinal))
            {
                var v=components.Select(c=>c.Features.SingleOrDefault(f=>f.Code==feature)?.RawValue).ToArray();
                var unavailable=components.Where(c=>!c.Features.Any(f=>f.Code==feature)).ToArray();
                diagnostics.Add(new(code,feature,v.Count(x=>x.HasValue),unavailable.Count(c=>c.Evidence.Status is not EvidenceStatus.Invalid and not EvidenceStatus.Stale),
                    unavailable.Count(c=>c.Evidence.Status==EvidenceStatus.Invalid),unavailable.Count(c=>c.Evidence.Status==EvidenceStatus.Stale),ResearchMath.Distribution(v),null,null));
            }
        }
        if(full)diagnostics.Add(new("FullEvidence100","Score",frozen.Count(x=>x.FullEvidence100.HasValue),frozen.Count(x=>x.FullEvidence100 is null),0,0,
            ResearchMath.Distribution(frozen.Select(x=>x.FullEvidence100)),null,null));
        var ic=metrics.Select(m=>
        {
            var p=m.DailyIc.Where(x=>x.PearsonIc.HasValue).Select(x=>x.PearsonIc!.Value).ToArray();var q=m.DailyIc.Where(x=>x.RankIc.HasValue).Select(x=>x.RankIc!.Value).ToArray();
            return new BaselineIcSummary(m.Fold,m.Variable,m.Horizon,m.Partition,p.Length,ResearchMath.Mean(p),ResearchMath.Median(p),ResearchMath.StdDev(p),
                p.Length==0?null:(decimal)p.Count(x=>x>0)/p.Length,ResearchMath.Mean(q),ResearchMath.Median(q),ResearchMath.StdDev(q),q.Length==0?null:(decimal)q.Count(x=>x>0)/q.Length);
        }).ToArray();
        var complete=frozen.Length>0&&frozen.All(x=>x.Components.All(c=>c.Evidence.Status==EvidenceStatus.Available&&c.Evidence.Value.HasValue))&&
            (!full||frozen.All(x=>x.FullEvidence100.HasValue))&&codes.All(code=>r.Protocol.OutcomeHorizons.All(h=>metrics.Any(m=>m.Variable==code&&m.Horizon==h&&
                m.OutcomePairCount>0&&m.DailyIc.Any(i=>i.PearsonIc.HasValue&&i.RankIc.HasValue)&&m.Quantiles.Length==r.Protocol.Quantiles&&m.Quantiles.All(q=>q.Count>0))));
        var time=metrics.SelectMany(m=>m.DailyIc.SelectMany(i=>new[]{0,(i.Date.Month-1)/3+1}.Select(quarter=>new{Metric=m,Daily=i,Quarter=quarter}))).GroupBy(x=>new{x.Metric.Variable,x.Metric.Horizon,x.Metric.Fold,x.Metric.Partition,x.Daily.Date.Year,x.Quarter})
            .Select(g=>(object)new{g.Key,Dates=g.Count(),MeanIC=ResearchMath.Mean(g.Where(x=>x.Daily.PearsonIc.HasValue).Select(x=>x.Daily.PearsonIc!.Value)),MeanRankIC=ResearchMath.Mean(g.Where(x=>x.Daily.RankIc.HasValue).Select(x=>x.Daily.RankIc!.Value))}).ToArray();
        // Correlation uses a common complete population, not a different pairwise denominator for each cell.
        var correlations=new List<ResearchCorrelation>();var orderedCodes=codes.Order(StringComparer.Ordinal).ToArray();
        foreach(var fold in r.Protocol.Folds)foreach(var partition in new[]{ResearchPartition.Train,ResearchPartition.Validation})
        {
            var period=ResearchProtocolRules.Period(fold,partition);
            var population=frozen.Where(x=>x.Date>=period.Start&&x.Date<=period.End&&x.Components.All(c=>c.Evidence.Value.HasValue&&c.Evidence.Status==EvidenceStatus.Available)).ToArray();
            for(int i=0;i<orderedCodes.Length;i++)for(int j=i+1;j<orderedCodes.Length;j++)
            {
                var left=orderedCodes[i];var right=orderedCodes[j];
                var daily=population.GroupBy(x=>x.Date).OrderBy(g=>g.Key).Select(g=>
                {
                    var x=g.Select(row=>row.Components.Single(c=>c.Evidence.Code.ToString()==left).Evidence.Value!.Value).ToArray();
                    var y=g.Select(row=>row.Components.Single(c=>c.Evidence.Code.ToString()==right).Evidence.Value!.Value).ToArray();
                    return new DailyIc(g.Key,x.Length,ResearchMath.Correlation(x,y),ResearchMath.RankIc(x,y));
                }).ToImmutableArray();
                correlations.Add(new(fold.Code,partition,left,right,daily));
            }
        }
        var run=new RealBaselineRun(r.ResearchRunId,r.CandidateVersion,r.Snapshots[0].Data.Identity.ParameterSetId,V3CandidateCalculator.ParameterChecksum(r.Parameters),
            a.Approval.FullApproval?.DatasetApprovalId.ToString()??a.Approval.ApprovalDigest,r.Protocol.Version,a.Approval.CoverageStart,a.Approval.CoverageEnd,
            ResearchMath.Digest(new{Approval=a.Approval.ApprovalDigest,r.CandidateVersion,r.Parameters,r.Protocol,Evidence=frozen}),"",r.CreatedAt,a.CompletedAt,complete?"BASELINE_COMPLETED":"BLOCKED_BY_DATA");
        var report=new BaselineResearchReport(run,frozen,diagnostics.ToArray(),metrics,ic,
            correlations.ToArray(),time,
            complete?["UNKNOWN: baseline requires interpretation; no automatic model decision"]:["DATA_GAP: insufficient complete features, outcome pairs or configured quantiles"]);
        return report with{Run=run with{OutputDigest=ResearchMath.Digest(report)}};
    }
}
