using System.Collections.Immutable;
using WorkJournal.Web.Models.Evidence;
using WorkJournal.Web.Models.HistoricalResearch;
using WorkJournal.Web.Models.Research;
using WorkJournal.Web.Services.Research;
namespace WorkJournal.Web.Services.HistoricalResearch;

public sealed record ValidationMetric(ResearchMetric Metric,decimal? MedianIc,decimal? IcStdDev,decimal? PositiveIcRatio,decimal? AblationRankIcDelta);
public sealed record EvidenceEvaluation(string Evidence,string FormulaStatus,string DataStatus,string ResearchStatus,string OosStatus,string PromotionDecision);
public sealed record YearDistribution(int Year,string Variable,ResearchDistribution Distribution);
public sealed record EvidenceValidationSummary(Guid ResearchRunId,string ResearchInputDigest,string ResearchOutputDigest,
    string CandidateVersion,string ResearchProtocolVersion,string OutcomeDefinitionVersion,string OutcomeSnapshotId,
    ImmutableArray<CandidateIdentity> Provenance,ImmutableArray<ValidationMetric> Metrics,
    ImmutableArray<YearDistribution> YearDistributions,ImmutableArray<ResearchCorrelation> Correlations,
    ImmutableArray<GroupMetric> GroupMetrics,ImmutableArray<EvidenceEvaluation> Evidence,
    int NumberOfCandidatesRegistered,int NumberOfCandidatesTested,int NumberOfParameterSetsTested,int NumberOfMetricsInspected,
    ImmutableArray<ResearchGap> Gaps,string PromotionStatus,string ThresholdStatus);

public static class EvidenceValidationReport
{
    // Factual diagnostics only; no thresholds or automatic promotion.
    public static EvidenceValidationSummary Create(ResearchRequest request,ResearchReport report,
        IEnumerable<ResearchCandidateRegistration> registrations,IReadOnlyCollection<ResearchReport> history)
    {
        var verified=new ResearchEngine().Run(request);
        if(ResearchMath.Digest(verified)!=ResearchMath.Digest(report))throw new InvalidDataException("Report/request provenance mismatch.");
        var registry=registrations.ToArray();
        var identities=report.Observations.Select(x=>x.Trace.Identity).Distinct().OrderBy(x=>x.TradeDate).ThenBy(x=>x.DatasetSnapshotId).ToImmutableArray();
        if(identities.Any(id=>!registry.Any(c=>c.CandidateVersion==request.CandidateVersion&&c.ParameterSetId==id.ParameterSetId&&c.ParameterChecksum==id.ParameterChecksum)))
            throw new InvalidDataException("Register every evaluated candidate/parameter identity first.");
        var gaps=report.Gaps.ToList();
        foreach(var kind in new[]{"Sector","MarketCap","Liquidity","Regime"})
            if(!request.Groups.Any(g=>g.Kind==kind))gaps.Add(new("CLASSIFICATION_GAP",kind+" diagnostics unavailable: PIT versioned historical groups not supplied."));
        var metrics=report.Metrics.Select(m=>
        {
            var ic=m.DailyIc.Where(x=>x.PearsonIc.HasValue).Select(x=>x.PearsonIc!.Value).ToArray();
            decimal? delta=null;
            if(m.Variable.StartsWith("Ablation.Without",StringComparison.Ordinal))
            {
                var baseline=report.Metrics.SingleOrDefault(x=>x.Fold==m.Fold&&x.Partition==m.Partition&&x.Horizon==m.Horizon&&x.Variable=="FullEvidence100");
                delta=baseline?.MeanRankIc-m.MeanRankIc;
            }
            if((m.Variable=="Momentum"||m.Variable=="RiskQuality")&&m.MeanRankIc<0)
                gaps.Add(new("NORMALIZATION_DIRECTION_CONFLICT",$"Observed negative mean RankIC: {m.Variable}/{m.Fold}/{m.Partition}/{m.Horizon}; diagnostic only, not a significance threshold."));
            return new ValidationMetric(m,ResearchMath.Median(ic),ResearchMath.StdDev(ic),ic.Length==0?null:(decimal)ic.Count(x=>x>0)/ic.Length,delta);
        }).ToImmutableArray();
        if(report.Correlations.Any(c=>c.Daily.Any(d=>d.PearsonIc.HasValue)))gaps.Add(new("REDUNDANCY_REVIEW_REQUIRED","Review reported correlations; no automatic exclusion or invented threshold."));
        var years=report.Observations.GroupBy(x=>x.Trace.Identity.TradeDate.Year).OrderBy(x=>x.Key)
            .SelectMany(g=>ResearchEngine.Variables().Select(v=>new YearDistribution(g.Key,v,ResearchMath.Distribution(g.Select(x=>ResearchEngine.Value(x.Trace,v)))))).ToImmutableArray();
        var statuses=Enum.GetValues<EvidenceCode>().Select(code=>
        {
            var name=code.ToString();var rows=report.Metrics.Where(m=>m.Variable==name).ToArray();
            var available=rows.Any(m=>m.AvailableCount>0);var paired=rows.Any(m=>m.OutcomePairCount>0);
            var oos=rows.Any(m=>m.Partition==ResearchPartition.Oos&&m.OutcomePairCount>0);
            return new EvidenceEvaluation(name,"IMPLEMENTED",available?rows.All(m=>m.Coverage==1)?"SUFFICIENT_FOR_RECORDED_SCOPE":"PARTIAL":"BLOCKED_BY_DATA",
                paired?"COMPLETED_NOT_VALIDATED":"NOT_ENOUGH_EVIDENCE",oos?"EXECUTED_NOT_VALIDATED":available?"PENDING":"NOT_ELIGIBLE",
                available?"RESEARCH_INSUFFICIENT":"BLOCKED_BY_DATA");
        }).ToImmutableArray();
        // Scope: supplied archived history, including failed/rejected reports, not a global lifetime claim.
        var runs=history.Append(report).GroupBy(r=>r.ResearchRunId).Select(g=>g.First()).ToArray();
        var traces=runs.SelectMany(r=>r.Observations).Select(o=>o.Trace).ToArray();
        return new(report.ResearchRunId,report.ResearchInputDigest,report.ResearchOutputDigest,request.CandidateVersion,request.Protocol.Version,
            "CloseReturn/FutureMaxCloseReturn/FutureMinCloseReturn-v1",request.Outcomes.SnapshotId,identities,metrics,years,report.Correlations,report.GroupMetrics,statuses,
            registry.Select(c=>c.CandidateVersion).Distinct().Count(),traces.Select(t=>t.CandidateVersion).Distinct().Count(),
            traces.Select(t=>t.Identity.ParameterSetId).Distinct().Count(),runs.Sum(r=>r.Metrics.Length),gaps.Distinct().ToImmutableArray(),"RESEARCH_INSUFFICIENT","UNRESOLVED");
    }
}
