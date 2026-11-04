using System.Collections.Immutable;
using WorkJournal.Web.Models.Evidence;
using WorkJournal.Web.Models.Research;
using WorkJournal.Web.Models.V2;
using WorkJournal.Web.Services.Evidence;
namespace WorkJournal.Web.Services.Research;

public sealed class ResearchEngine
{
    public ResearchReport Run(ResearchRequest request)
    {
        Validate(request);
        var observations=new List<ResearchObservation>();var gaps=new List<ResearchGap>();
        var folds=request.Protocol.Folds.OrderBy(f=>f.Oos.Start).ToArray();
        bool In(DateOnly d,ResearchPeriod p)=>d>=p.Start&&d<=p.End;
        foreach(var snapshot in request.Snapshots.OrderBy(s=>s.Data.Identity.TradeDate))
        {
            var d=snapshot.Data.Identity.TradeDate;
            if(!folds.Any(f=>In(d,f.Train)||In(d,f.Validation)||(request.EvaluateOos&&In(d,f.Oos))))continue;
            var proof=snapshot.Evidence;
            if(new[]{proof.Reference,proof.HistoricalUniverseEvidence,proof.HistoricalBenchmarkEvidence,proof.AvailabilityEvidence,proof.RevisionEvidence,proof.PriceBasisEvidence}.Any(string.IsNullOrWhiteSpace))
            { gaps.Add(new("PIT_DATA_GAP","Snapshot "+snapshot.Data.Identity.DatasetSnapshotId+" lacks source evidence; not evaluated."));continue; }
            foreach(var instrument in snapshot.Data.UniverseMembers.Order())
            {
                var trace=new V3CandidateCalculator().Calculate(snapshot.Data,instrument,request.Parameters);
                var anchors=snapshot.Data.Series.Where(s=>s.InstrumentId==instrument).SelectMany(s=>s.Bars).Where(b=>b.Date==d&&b.AvailableAt.HasValue&&b.AvailableAt<=snapshot.Data.Identity.DataCutoff&&b.Status==EvidenceStatus.Available).ToArray();
                decimal? anchor=anchors.Length==1?anchors[0].Close:null;
                var outcomes=request.Protocol.OutcomeHorizons.Order().Select(h=>{
                    var index=request.Outcomes.Sessions.IndexOf(d);
                    DateOnly? end=h<request.Outcomes.Sessions.Length-index?request.Outcomes.Sessions[index+h]:null;
                    var allowed=end.HasValue&&folds.Any(f=>new[]{f.Train,f.Validation}.Concat(request.EvaluateOos?new[]{f.Oos}:Array.Empty<ResearchPeriod>()).Any(p=>In(d,p)&&end<=p.End));
                    return allowed?ResearchOutcomeCalculator.Calculate(trace,h,request.Outcomes,anchor):new ResearchOutcome(h,end,null,null,null,end.HasValue?"PURGED_PARTITION_BOUNDARY":"INSUFFICIENT_FUTURE_SESSIONS");
                }).ToImmutableArray();
                observations.Add(new(request.ResearchRunId,trace,outcomes));
            }
        }
        var rows=observations.ToImmutableArray();
        var metrics=new List<ResearchMetric>();var correlations=new List<ResearchCorrelation>();var grouped=new List<GroupMetric>();
        var variables=Variables();
        foreach(var fold in folds)
        {
            foreach(var partition in new[]{ResearchPartition.Train,ResearchPartition.Validation})
                Analyze(fold,partition);
            if(request.EvaluateOos)
            {
                var receipts=request.Selections.Where(s=>s.Fold==fold.Code).ToArray();
                if(receipts.Length!=1)throw new ArgumentException("Exactly one frozen selection required per OOS fold.");
                var receipt=receipts[0];var card=Scorecard(request,metrics,fold.Code,receipt.DevelopmentRunId);
                var selected=ResearchProtocolRules.Select(card,receipt.SelectedAt,receipt.Rationale);
                if(selected!=receipt || DateOnly.FromDateTime(receipt.SelectedAt.UtcDateTime)>=fold.Oos.Start)
                    throw new ArgumentException("OOS selection was not frozen from matching development evidence before OOS.");
                Analyze(fold,ResearchPartition.Oos);
            }
        }
        void Analyze(ResearchFold fold,ResearchPartition partition)
        {
            var period=ResearchProtocolRules.Period(fold,partition);
            var periodRows=rows.Where(o=>In(o.Trace.Identity.TradeDate,period)).ToArray();
            if(periodRows.Length==0)gaps.Add(new("EMPTY_PARTITION",fold.Code+" "+partition+" has no usable observations."));
            foreach(var variable in variables)
            foreach(var h in request.Protocol.OutcomeHorizons.Order())
            {
                var values=periodRows.Select(o=>{
                    var label=o.Outcomes.Single(x=>x.Horizon==h);
                    var permitted=label.LabelEnd.HasValue&&label.LabelEnd<=period.End;
                    return new ResearchValue(o.Trace.Identity.TradeDate,o.Trace.InstrumentId,Value(o.Trace,variable),permitted?label.FutureReturn:null,null);
                }).ToArray();
                var purged=periodRows.Count(o=>o.Outcomes.Single(x=>x.Horizon==h).LabelEnd is {} end&&end>period.End);
                metrics.Add(ResearchMetrics.Calculate(fold.Code,partition,variable,h,values,request.Protocol.Quantiles,purged));
                // Metadata is point-in-time; no current-sector or inferred regime fallback.
                foreach(var group in request.Groups.GroupBy(g=>(g.Kind,g.Value,g.DefinitionVersion)).OrderBy(g=>g.Key.Kind,StringComparer.Ordinal).ThenBy(g=>g.Key.Value,StringComparer.Ordinal).ThenBy(g=>g.Key.DefinitionVersion,StringComparer.Ordinal))
                {
                    var keys=group.Where(g=>g.AvailableAt.HasValue).Select(g=>(g.Date,g.InstrumentId)).ToHashSet();
                    var filtered=values.Where(v=>keys.Contains((v.Date,v.InstrumentId))).ToArray();
                    var groupPurged=periodRows.Count(o=>keys.Contains((o.Trace.Identity.TradeDate,o.Trace.InstrumentId))&&o.Outcomes.Single(x=>x.Horizon==h).LabelEnd is {} end&&end>period.End);
                    if(filtered.Length>0)grouped.Add(new(group.Key.Kind,group.Key.Value,group.Key.DefinitionVersion,ResearchMetrics.Calculate(fold.Code,partition,variable,h,filtered,request.Protocol.Quantiles,groupPurged)));
                }
            }
            var correlationVariables=variables.Where(v=>!v.StartsWith("Ablation.")&&!v.StartsWith("Raw.")&&v!="FullEvidence100").ToArray();
            for(int a=0;a<correlationVariables.Length;a++)for(int b=a+1;b<correlationVariables.Length;b++)
            {
                var left=correlationVariables[a];var right=correlationVariables[b];
                var daily=periodRows.GroupBy(o=>o.Trace.Identity.TradeDate).OrderBy(g=>g.Key).Select(g=>{
                    var pairs=g.Select(o=>(X:Value(o.Trace,left),Y:Value(o.Trace,right))).Where(v=>v.X.HasValue&&v.Y.HasValue).ToArray();
                    var x=pairs.Select(v=>v.X!.Value).ToArray();var y=pairs.Select(v=>v.Y!.Value).ToArray();
                    return new DailyIc(g.Key,pairs.Length,ResearchMath.Correlation(x,y),ResearchMath.RankIc(x,y));
                }).ToImmutableArray();
                correlations.Add(new(fold.Code,partition,left,right,daily));
            }
        }
        if(request.Snapshots.Any(s=>s.Evidence.Synthetic))gaps.Add(new("SYNTHETIC_ONLY","Fixture calculations are not historical validation or promotion evidence."));
        gaps.Add(new("EXTERNAL_PIT_CERTIFICATION_PENDING","Source references are recorded, not independently certified by this engine."));
        gaps.Add(new("MODEL_REVIEW_PENDING","No automatic validation, promotion, formula selection, or production threshold."));
        if(!request.EvaluateOos)gaps.Add(new("OOS_NOT_EVALUATED","Development-only run; frozen selection required."));
        foreach(var kind in new[]{"Regime","Sector","Liquidity","MarketCap"})
            if(!request.Groups.Any(g=>g.Kind==kind))gaps.Add(new(kind.ToUpperInvariant()+"_UNAVAILABLE","No versioned PIT "+kind+" groups supplied; no thresholds/classifications invented."));
        foreach(var code in Enum.GetValues<EvidenceCode>())
            foreach(var reason in rows.SelectMany(o=>o.Trace.Components).Where(c=>c.Evidence.Code==code&&c.Evidence.Value is null).Select(c=>c.Evidence.ReasonCode).Distinct().Order(StringComparer.Ordinal))gaps.Add(new(code+"_GAP",reason));
        if(rows.IsEmpty)gaps.Add(new("NO_OBSERVATIONS","No usable historical evidence; run is Rejected, not Completed with empty results."));
        var statuses=Enum.GetValues<EvidenceCode>().Select(code=>{
            var entries=rows.SelectMany(o=>o.Trace.Components).Where(c=>c.Evidence.Code==code).ToArray();
            var available=entries.Count(c=>c.Evidence.Value.HasValue);
            return new ComponentResearchStatus(code.ToString(),available==0?"BLOCKED_BY_DATA":"CANDIDATE",available+" / "+entries.Length+" available; statistical review and validation pending.");
        }).ToImmutableArray();
        var inputDigest=ResearchMath.Digest(CanonicalRequest(request));
        var result=new ResearchReport(request.ResearchRunId,inputDigest,"",rows.IsEmpty?ResearchRunStatus.Rejected:ResearchRunStatus.Completed,true,rows,
            metrics.ToImmutableArray(),correlations.ToImmutableArray(),grouped.ToImmutableArray(),gaps.Distinct().OrderBy(g=>g.Code,StringComparer.Ordinal).ThenBy(g=>g.Detail,StringComparer.Ordinal).ToImmutableArray(),statuses,
            statuses.Any(s=>s.Status=="BLOCKED_BY_DATA")?"BLOCKED_BY_DATA":"RESEARCH_INSUFFICIENT");
        return result with {ResearchOutputDigest=ResearchMath.Digest(result)};
    }

    public static DevelopmentScorecard Scorecard(ResearchRequest request,IEnumerable<ResearchMetric> metrics,string fold,Guid? developmentRunId=null)
    {
        var f=request.Protocol.Folds.Single(x=>x.Code==fold);
        var train=metrics.Where(x=>x.Fold==fold&&x.Partition==ResearchPartition.Train).ToImmutableArray();
        var validation=metrics.Where(x=>x.Fold==fold&&x.Partition==ResearchPartition.Validation).ToImmutableArray();
        var id=request.Snapshots[0].Data.Identity;
        var known=request.Outcomes.Series.SelectMany(s=>s.Closes).Where(c=>c.Date>=f.Train.Start&&c.Date<=f.Validation.End&&c.AvailableAt<=request.Outcomes.EvaluationCutoff)
            .Where(c=>c.AvailableAt.HasValue).Select(c=>c.AvailableAt!.Value).DefaultIfEmpty(request.Outcomes.EvaluationCutoff).Max();
        var canonical=CanonicalRequest(request);
        bool InDevelopment(DateOnly d)=>(d>=f.Train.Start&&d<=f.Train.End)||(d>=f.Validation.Start&&d<=f.Validation.End);
        var inputDigest=ResearchMath.Digest(new {request.CandidateVersion,request.ModelVersion,request.FeatureVersion,request.Parameters,Protocol=request.Protocol with {Folds=[f]},
            Snapshots=canonical.Snapshots.Where(s=>InDevelopment(s.Data.Identity.TradeDate)).ToArray(),
            OutcomeVersion=request.Outcomes.RevisionVersion,OutcomeSnapshot=request.Outcomes.SnapshotId,
            Closes=canonical.Outcomes.Series.Select(s=>s with {Closes=s.Closes.Where(c=>InDevelopment(c.Date)&&c.AvailableAt<=request.Outcomes.EvaluationCutoff).ToImmutableArray()}).ToArray(),
            Policies=canonical.AvailabilityPolicies,Groups=canonical.Groups.Where(g=>InDevelopment(g.Date)).ToArray()});
        var card=new DevelopmentScorecard(developmentRunId??request.ResearchRunId,id.ParameterSetId,id.ParameterChecksum,request.CandidateVersion,fold,known,inputDigest,train,validation,"");
        return card with {Digest=ResearchMath.Digest(card)};
    }
    public static ImmutableArray<string> Variables()
    {
        var features=CandidateFeatureCatalog.Create(Guid.Empty).Select(f=>f.Code).ToArray();
        return Enum.GetNames<EvidenceCode>().Concat(new[]{"FullEvidence100"}).Concat(features.Select(f=>"Feature."+f)).Concat(features.Select(f=>"Raw."+f))
            .Concat(Enum.GetNames<EvidenceCode>().Select(c=>"Ablation.Without"+c)).Concat(new[]{"Alignment","Position","Slope","Structure"}.Select(f=>"Ablation.TrendWithout"+f)).ToImmutableArray();
    }
    public static decimal? Value(CandidateTrace t,string variable)
    {
        if(variable=="FullEvidence100")return t.Score.TotalScore;
        if(Enum.TryParse<EvidenceCode>(variable,out var code))return t.Components.Single(c=>c.Evidence.Code==code).Evidence.Value;
        if(variable.StartsWith("Feature.")||variable.StartsWith("Raw."))
        {
            var raw=variable.StartsWith("Raw.");var name=variable[(raw?4:8)..];
            var f=t.Components.SelectMany(c=>c.Features).SingleOrDefault(f=>f.Code==name);
            return raw?f?.RawValue:f?.NormalizedValue;
        }
        if(variable.StartsWith("Ablation.Without"))
        {
            var omitted=Enum.Parse<EvidenceCode>(variable["Ablation.Without".Length..]);
            // Matched complete observations only. This is a lower-scale research contrast, never Score100.
            return t.Score.TotalScore-t.Score.Components.Single(c=>c.Code==omitted).WeightedPoints;
        }
        if(variable.StartsWith("Ablation.TrendWithout"))
        {
            var name=variable["Ablation.TrendWithout".Length..];var c=t.Components.Single(c=>c.Evidence.Code==EvidenceCode.Trend);
            var weight=name switch {"Alignment"=>.30m,"Position"=>.25m,"Slope"=>.25m,"Structure"=>.20m,_=>throw new ArgumentException("Unknown ablation")};
            return c.Evidence.Value-c.Features.SingleOrDefault(f=>f.Code=="Trend."+name)?.NormalizedValue*weight;
        }
        throw new ArgumentException("Unknown research variable.");
    }
    public static ResearchRequest CanonicalRequest(ResearchRequest r)=>r with {
        Protocol=r.Protocol with {Folds=r.Protocol.Folds.OrderBy(f=>f.Oos.Start).ToImmutableArray(),OutcomeHorizons=r.Protocol.OutcomeHorizons.Order().ToImmutableArray()},
        Snapshots=r.Snapshots.OrderBy(s=>s.Data.Identity.TradeDate).Select(s=>s with {Data=s.Data with {
            UniverseMembers=s.Data.UniverseMembers.Order().ToImmutableArray(),Sessions=s.Data.Sessions.Order().ToImmutableArray(),
            Series=s.Data.Series.OrderBy(x=>x.InstrumentId).Select(x=>x with {Bars=x.Bars.OrderBy(b=>b.Date).ThenBy(b=>b.AvailableAt).ToImmutableArray(),Flows=x.Flows.OrderBy(f=>f.Date).ThenBy(f=>f.AvailableAt).ToImmutableArray()}).ToImmutableArray(),
            Benchmark=s.Data.Benchmark.OrderBy(b=>b.Date).ThenBy(b=>b.AvailableAt).ToImmutableArray()}}).ToImmutableArray(),
        Outcomes=r.Outcomes with {Series=r.Outcomes.Series.OrderBy(s=>s.InstrumentId).Select(s=>s with {Closes=s.Closes.OrderBy(c=>c.Date).ThenBy(c=>c.AvailableAt).ToImmutableArray()}).ToImmutableArray()},
        Groups=r.Groups.OrderBy(g=>g.Kind,StringComparer.Ordinal).ThenBy(g=>g.DefinitionVersion,StringComparer.Ordinal).ThenBy(g=>g.Date).ThenBy(g=>g.InstrumentId).ToImmutableArray(),
        AvailabilityPolicies=r.AvailabilityPolicies.OrderBy(p=>p.Provider,StringComparer.Ordinal).ThenBy(p=>p.DatasetType,StringComparer.Ordinal).ThenBy(p=>p.PolicyVersion,StringComparer.Ordinal).ToImmutableArray(),
        Selections=r.Selections.IsDefault?[]:r.Selections.OrderBy(s=>s.Fold,StringComparer.Ordinal).ToImmutableArray()
    };
    private static void Validate(ResearchRequest r)
    {
        ArgumentNullException.ThrowIfNull(r);ArgumentNullException.ThrowIfNull(r.Parameters);ArgumentNullException.ThrowIfNull(r.Protocol);ArgumentNullException.ThrowIfNull(r.Outcomes);
        if(r.ResearchRunId==Guid.Empty||r.CandidateVersion!=V3CandidateCalculator.Version||string.IsNullOrWhiteSpace(r.ModelVersion)||string.IsNullOrWhiteSpace(r.FeatureVersion)||r.Snapshots.IsDefaultOrEmpty||r.Groups.IsDefault||r.AvailabilityPolicies.IsDefault)
            throw new ArgumentException("Explicit run identity and arrays required.");
        ResearchProtocolRules.Validate(r.Protocol,r.Outcomes.Sessions);
        if(string.IsNullOrWhiteSpace(r.Outcomes.SnapshotId)||string.IsNullOrWhiteSpace(r.Outcomes.RevisionVersion)||r.Outcomes.Series.IsDefault||r.Outcomes.Series.Select(s=>s.InstrumentId).Distinct().Count()!=r.Outcomes.Series.Length)
            throw new ArgumentException("Versioned outcome snapshot required.");
        if(r.Snapshots.Select(s=>s.Data.Identity.TradeDate).Distinct().Count()!=r.Snapshots.Length)throw new ArgumentException("One historical cross-section per date required.");
        if(r.Snapshots.Select(s=>s.Data.Identity.ParameterSetId).Distinct().Count()!=1||r.Snapshots.Any(s=>s.Data.Identity.ParameterChecksum!=V3CandidateCalculator.ParameterChecksum(r.Parameters)))throw new ArgumentException("Run parameter identity mismatch.");
        foreach(var s in r.Snapshots)
        {
            var date=s.Data.Identity.TradeDate;
            if(!r.Outcomes.Sessions.Contains(date)||s.Data.Sessions.Any(d=>d<=date&&!r.Outcomes.Sessions.Contains(d)))throw new ArgumentException("Evidence/outcome calendar mismatch.");
            if(s.Data.Identity.DataCutoff>r.Outcomes.EvaluationCutoff)throw new ArgumentException("Evaluation precedes evidence cutoff.");
        }
        foreach(var p in r.AvailabilityPolicies)
            if(new[]{p.Provider,p.DatasetType,p.PolicyVersion,p.AvailabilityRule,p.EvidenceSource}.Any(string.IsNullOrWhiteSpace)||p.EffectiveTo<p.EffectiveFrom)throw new ArgumentException("Availability policy requires source evidence and effective period.");
        if(r.Groups.GroupBy(g=>(g.InstrumentId,g.Date,g.Kind)).Any(g=>g.Count()>1))throw new ArgumentException("Ambiguous historical groups.");
        foreach(var g in r.Groups)
        {
            var s=r.Snapshots.SingleOrDefault(s=>s.Data.Identity.TradeDate==g.Date);
            if(s is null||!s.Data.UniverseMembers.Contains(g.InstrumentId)||g.AvailableAt is null||g.AvailableAt>s.Data.Identity.DataCutoff||
                new[]{g.Kind,g.Value,g.DefinitionVersion}.Any(string.IsNullOrWhiteSpace))throw new ArgumentException("Group classification must be versioned historical PIT evidence.");
        }
        if(r.EvaluateOos&&r.Selections.IsDefaultOrEmpty)throw new ArgumentException("OOS requires frozen development selection.");
    }
}
