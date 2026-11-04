using System.Collections.Immutable;
using WorkJournal.Web.Models.Evidence;
using WorkJournal.Web.Models.Research;
using WorkJournal.Web.Services.Evidence;
using WorkJournal.Web.Services.Research;
namespace WorkJournal.Domain.Research;
public sealed record StabilityDiagnostic(string Period,int Observations,decimal? Ic,decimal? RankIc);
public sealed record CrossSectionalDiagnostic(DateOnly Date,ResearchDistribution Distribution);
public sealed record ValidationDiagnostic(string Name,string DefinitionVersion,string Status,int ObservationCount,int ValidObservationCount,int MissingCount,
    ResearchDistribution Distribution,decimal? Ic,decimal? RankIc,ImmutableArray<QuantileResult> QuantileReturns,
    StabilityDiagnostic[] YearlyIc,StabilityDiagnostic[] QuarterlyIc,decimal? SaturationLow,decimal? SaturationHigh,
    decimal? DirectionConsistency,decimal? Monotonicity,DateOnly? CoverageStart,DateOnly? CoverageEnd,ImmutableArray<DailyIc> DailyIc,CrossSectionalDiagnostic[] CrossSectionalDistribution);
public sealed record FeatureValidationResult(string FeatureName,ValidationDiagnostic Raw,ValidationDiagnostic Normalized,string[] Warnings);
public sealed record EvidenceValidationResult(string Evidence,string Interpretation,ValidationDiagnostic Validation);
public sealed record FullEvidenceValidationResult(string ModelVersion,ValidationDiagnostic Validation);
public sealed record CorrelationDiagnostic(string Left,string Right,ImmutableArray<DailyIc> Daily,decimal? Pearson,decimal? Spearman);
public sealed record AblationDiagnostic(string? RemovedEvidence,int AvailableWeight,ValidationDiagnostic Metrics);
public sealed record FormulaResearchPackage(string FormulaDigest,string Scope,string Status,FeatureValidationResult[] Features,
    EvidenceValidationResult[] Evidence,FullEvidenceValidationResult Full,CorrelationDiagnostic[] Correlations,AblationDiagnostic[] Ablations,
    string RegimeStatus,string RiskOutcomeStatus);
public static class FormulaDiagnostics
{
    public static ValidationDiagnostic Validate(string name,IEnumerable<ResearchValue> source,int horizon,int quantiles,bool normalized)
    {
        var rows=source.OrderBy(x=>x.Date).ThenBy(x=>x.InstrumentId).ToArray();
        if(rows.GroupBy(x=>(x.Date,x.InstrumentId)).Any(g=>g.Count()>1))throw new ArgumentException("Duplicate observation");
        if(normalized&&rows.Any(x=>x.Value is <0 or >1))throw new ArgumentException("Normalized value outside [0,1]");
        var metric=ResearchMetrics.Calculate("diagnostic",ResearchPartition.Train,name,horizon,rows,quantiles,0);
        var values=rows.Where(x=>x.Value.HasValue).ToArray();
        StabilityDiagnostic[] Time(Func<DateOnly,string> key)=>rows.GroupBy(x=>key(x.Date)).OrderBy(g=>g.Key,StringComparer.Ordinal).Select(g=>{
            var m=ResearchMetrics.Calculate(g.Key,ResearchPartition.Train,name,horizon,g,quantiles,0);
            return new StabilityDiagnostic(g.Key,m.ObservationCount,m.MeanIc,m.MeanRankIc);
        }).ToArray();
        var years=Time(d=>d.Year.ToString(System.Globalization.CultureInfo.InvariantCulture));
        var yearValues=years.Where(x=>x.Ic.HasValue&&x.Ic!=0).ToArray();
        return new(name,V3CandidateCalculator.Version,rows.Length==0?"NOT_RUN":metric.OutcomePairCount<2?"INSUFFICIENT":"COMPLETED",
            rows.Length,values.Length,rows.Length-values.Length,metric.Distribution,metric.MeanIc,metric.MeanRankIc,metric.Quantiles,
            years,Time(d=>$"{d.Year}-Q{(d.Month-1)/3+1}"),normalized&&values.Length>0?(decimal)values.Count(x=>x.Value==0)/values.Length:null,
            normalized&&values.Length>0?(decimal)values.Count(x=>x.Value==1)/values.Length:null,
            yearValues.Length==0||metric.MeanIc is null or 0?null:(decimal)yearValues.Count(x=>Math.Sign(x.Ic!.Value)==Math.Sign(metric.MeanIc.Value))/yearValues.Length,
            metric.AdjacentMonotonicity,rows.Length==0?null:rows[0].Date,rows.Length==0?null:rows[^1].Date,metric.DailyIc,
            rows.GroupBy(x=>x.Date).Select(g=>new CrossSectionalDiagnostic(g.Key,ResearchMath.Distribution(g.Select(x=>x.Value)))).ToArray());
    }
    // One declared fold/partition with purged labels; never mix OOS into development.
    public static FormulaResearchPackage Build(IEnumerable<ResearchObservation> input,int horizon,int quantiles,string scope)
    {
        if(string.IsNullOrWhiteSpace(scope)||horizon<=0)throw new ArgumentException("Explicit scope/horizon required");
        var rows=input.OrderBy(x=>x.Trace.Identity.TradeDate).ThenBy(x=>x.Trace.InstrumentId).ToArray();
        ResearchValue[] Values(Func<CandidateTrace,decimal?> select)=>rows.Select(o=>new ResearchValue(o.Trace.Identity.TradeDate,o.Trace.InstrumentId,select(o.Trace),o.Outcomes.SingleOrDefault(x=>x.Horizon==horizon)?.FutureReturn,null)).ToArray();
        decimal? Evidence(CandidateTrace t,EvidenceCode code)=>t.Components.SingleOrDefault(x=>x.Evidence.Code==code)?.Evidence.Value;
        var features=CandidateFeatureCatalog.Create(Guid.Empty).Select(f=>{
            CandidateFeature? Find(CandidateTrace t)=>t.Components.SelectMany(x=>x.Features).SingleOrDefault(x=>x.Code==f.Code);
            var raw=Validate(f.Code+".Raw",Values(t=>Find(t)?.RawValue),horizon,quantiles,false);
            var norm=Validate(f.Code,Values(t=>Find(t)?.NormalizedValue),horizon,quantiles,true);
            return new FeatureValidationResult(f.Code,raw,norm,norm.SaturationLow>0||norm.SaturationHigh>0?["SATURATION_PRESENT_REVIEW_REQUIRED"]:[]);
        }).ToArray();
        var codes=Enum.GetValues<EvidenceCode>();
        var evidence=codes.Select(c=>new EvidenceValidationResult(c.ToString(),c==EvidenceCode.RiskQuality?
            "Security-level risk/quality; not portfolio risk, sizing or execution risk":"Evidence strength; not a trade instruction",
            Validate(c.ToString(),Values(t=>Evidence(t,c)),horizon,quantiles,true))).ToArray();
        var full=Validate("FullEvidence100",Values(t=>t.Score.TotalScore),horizon,quantiles,false);
        var ablations=codes.Select(c=>new AblationDiagnostic(c.ToString(),100-FullEvidenceModel.Weights[c],
            Validate("Without"+c,Values(t=>t.Score.TotalScore is {} total&&Evidence(t,c) is {} v?total-v*FullEvidenceModel.Weights[c]:null),horizon,quantiles,false))).Prepend(new(null,100,full)).ToArray();
        var variables=features.Select(f=>(f.FeatureName,Values(t=>t.Components.SelectMany(c=>c.Features).SingleOrDefault(x=>x.Code==f.FeatureName)?.NormalizedValue)))
            .Concat(codes.Select(c=>(c.ToString(),Values(t=>Evidence(t,c))))).ToArray();
        var correlations=new List<CorrelationDiagnostic>();
        for(var i=0;i<variables.Length;i++)for(var j=i;j<variables.Length;j++)
        {
            var pairs=variables[i].Item2.Zip(variables[j].Item2).Where(x=>x.First.Value.HasValue&&x.Second.Value.HasValue).ToArray();
            var daily=rows.Select(x=>x.Trace.Identity.TradeDate).Distinct().Select(d=>{
                var p=pairs.Where(x=>x.First.Date==d).ToArray();var x=p.Select(x=>x.First.Value!.Value).ToArray();var y=p.Select(x=>x.Second.Value!.Value).ToArray();
                return new DailyIc(d,p.Length,ResearchMath.Correlation(x,y),ResearchMath.RankIc(x,y));
            }).ToImmutableArray();
            correlations.Add(new(variables[i].Item1,variables[j].Item1,daily,ResearchMath.Mean(daily.Where(x=>x.PearsonIc.HasValue).Select(x=>x.PearsonIc!.Value)),ResearchMath.Mean(daily.Where(x=>x.RankIc.HasValue).Select(x=>x.RankIc!.Value))));
        }
        return new(FormulaCatalog.Identity.FormulaDigest,scope,rows.Length==0?"NOT_RUN":"DIAGNOSTICS_COMPUTED_NOT_VALIDATED",features,evidence,
            new(V3CandidateCalculator.Version,full),correlations.ToArray(),ablations,"REQUIRES_PIT_REGIME_LABELS","REQUIRES_FUTURE_RISK_LABELS");
    }
}
