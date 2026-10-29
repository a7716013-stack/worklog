using System.Collections.Immutable;
using System.Text.Json;
using WorkJournal.Web.Models.Evidence;
using WorkJournal.Web.Models.Research;
using WorkJournal.Web.Models.V2;
using WorkJournal.Web.Services.Evidence;
using WorkJournal.Web.Services.Research;
int checks=0;
void Check(bool yes,string name){if(!yes)throw new Exception(name);checks++;Console.WriteLine("PASS: "+name);}
void Near(decimal? actual,decimal expected,string name)=>Check(actual.HasValue&&Math.Abs(actual.Value-expected)<.000000000001m,name);
void Reject(Action action,string name){try{action();}catch(Exception e) when(e is ArgumentException or InvalidOperationException or IOException or InvalidDataException){Check(true,name);return;}throw new Exception("Expected rejection: "+name);}
Near(ResearchMath.Correlation([1,2,3],[2,4,6]),1,"Pearson positive golden");
Near(ResearchMath.Correlation([1,2,3],[3,2,1]),-1,"Pearson negative golden");
Check(ResearchMath.Correlation([1,1],[1,2]) is null,"constant IC is null not zero");
Check(ResearchMath.Correlation([1],[1]) is null,"singleton IC unavailable");
Reject(()=>ResearchMath.Correlation([1,2],[1]),"unpaired correlation rejected");
Near(ResearchMath.RankIc([10,10,30],[1,1,3]),1,"Spearman average ties");
Check(ResearchMath.Ranks([2,2,5]).SequenceEqual(new decimal[]{1.5m,1.5m,3}),"midrank ties");
Check(ResearchMath.Quantiles([1,2,3,4,5],5).SequenceEqual(new[]{1,2,3,4,5}),"quantile golden");
Check(ResearchMath.Quantiles([7,7,7],5).All(q=>q==3),"all ties same quantile");
Near(ResearchMath.Percentile([0,10,20,30],.25m),7.5m,"linear order statistic");
var distribution=ResearchMath.Distribution(new decimal?[]{0,10,20,null});
Near(distribution.MissingRate,.25m,"distribution missing denominator");
Near(distribution.P50,10,"distribution median");
Check(ResearchMath.Distribution(new decimal?[]{null}).Mean is null,"empty distribution stays null");
Check(ResearchMath.Digest(new{Value=1.0m})==ResearchMath.Digest(new{Value=1.00m}),"canonical decimal scale");
Check(ResearchMath.Canonical(new Dictionary<string,int>{{"b",2},{"a",1}})==ResearchMath.Canonical(new Dictionary<string,int>{{"a",1},{"b",2}}),"canonical object ordering");
var ids=Enumerable.Range(1,3).Select(n=>Guid.Parse($"10000000-0000-0000-0000-{n:000000000000}")).ToImmutableArray();
var dates=Enumerable.Range(0,155).Select(i=>new DateOnly(2020,1,1).AddDays(i)).ToImmutableArray();
DateTimeOffset At(DateOnly d)=>new(d.ToDateTime(new TimeOnly(18,0)),TimeSpan.Zero);
var p=new V3EvidenceCandidateV1ParameterSet{
    SlopeHorizon=5,StructureHorizon=10,AlignmentBounds=new(-.1m,.1m),PositionBounds=new(-.1m,.1m),SlopeBounds=new(-.1m,.1m),
    RocHorizon=5,MacdBounds=new(-.01m,.01m),RocBounds=new(-.1m,.1m),VolumeHorizon=5,ReturnHorizon=5,BreakoutHorizon=5,
    RvolLogBounds=new(-1,1),PvcBounds=new(-.1m,.1m),BreakoutBounds=new(-.1m,.1m),InstitutionalHorizons=[2,3,5],PersistenceHorizon=5,Persistence=PersistenceGroup.Combined,
    RelativeStrengthHorizons=[2,3,5],VolatilityHorizon=5,Variance=VarianceDefinition.Population,DrawdownHorizon=5,AtrPeriod=5,Atr=AtrDefinition.SimpleMean,LiquidityHorizon=5,LiquidityPrice="Close"};
var parameterId=Guid.NewGuid();var benchmarkId=Guid.NewGuid();var versionId=Guid.NewGuid();
decimal Close(int instrument,int day)=>100+instrument*day*.05m+(day%3)*.1m;
var allSeries=ids.Select((id,k)=>new CandidateSeries(id,dates.Select((d,i)=>new CandidateBar(d,Close(k+1,i),Close(k+1,i)+1,Close(k+1,i)-1,1000+i*(k+1),At(d))).ToImmutableArray(),
    dates.Select((d,i)=>new CandidateFlow(d,(k+1)*10+i%2,5-k,1,At(d))).ToImmutableArray())).ToImmutableArray();
var snapshots=Enumerable.Range(130,21).Select(i=>{
    var d=dates[i];var identity=new CandidateIdentity(Guid.NewGuid(),Guid.NewGuid(),benchmarkId,versionId,parameterId,V3CandidateCalculator.ParameterChecksum(p),"synthetic-dataset-"+i,"synthetic-universe-"+i,"synthetic-benchmark","synthetic-unadjusted-v1",d,At(d).AddHours(1));
    return new ResearchSnapshot(new(identity,FoundationStatus.Finalized,At(d),At(dates[0]),dates.Take(i+1).ToImmutableArray(),ids,
        allSeries.Select(s=>s with{Bars=s.Bars.Take(i+1).ToImmutableArray(),Flows=s.Flows.Take(i+1).ToImmutableArray()}).ToImmutableArray(),allSeries[0].Bars.Take(i+1).ToImmutableArray()),
        new("synthetic fixture","synthetic universe","synthetic benchmark","synthetic publication","synthetic revision","synthetic price basis",true));
}).ToImmutableArray();
var fold=new ResearchFold("F1",new(dates[130],dates[135]),new(dates[138],dates[143]),new(dates[146],dates[150]));
var protocol=new ResearchProtocol("ResearchFixture-v1",[fold],[1,2],3,2);
var outcomes=new OutcomeDataset("synthetic-outcomes-v1","fixture-fixed-revision",At(dates[^1]),dates,
    allSeries.Select(s=>new OutcomeSeries(s.InstrumentId,"synthetic-unadjusted-v1",s.Bars.Select(b=>new OutcomeClose(b.Date,b.Close,b.AvailableAt,b.Status)).ToImmutableArray())).ToImmutableArray());
var request=new ResearchRequest(Guid.NewGuid(),At(dates[^1]),V3CandidateCalculator.Version,"ResearchCandidate-3.0.4","CandidateFeatures-v1",p,protocol,snapshots,outcomes,[],[]);
var engine=new ResearchEngine();var report=engine.Run(request);
Check(report.Status==ResearchRunStatus.Completed&&report.Finalized,"completed deterministic development run");
Check(report.PromotionStatus=="RESEARCH_INSUFFICIENT","synthetic results never promoted");
Check(report.Metrics.All(m=>m.Partition!=ResearchPartition.Oos),"development excludes OOS metrics");
Check(report.Observations.All(o=>o.Trace.Identity.TradeDate<fold.Oos.Start),"development excludes OOS feature observations");
Check(report.Observations.Any(o=>o.Outcomes.Any(l=>l.Status=="PURGED_PARTITION_BOUNDARY")),"purge future labels at partition boundary");
Check(report.Observations.Where(o=>o.Trace.Identity.TradeDate==fold.Validation.End).All(o=>o.Outcomes.All(l=>l.FutureReturn is null)),"validation-end labels never expose OOS");
Check(report.Metrics.Any(m=>m.DailyIc.Length>1),"IC is reported per date");
Check(report.Metrics.Any(m=>m.Variable=="Ablation.WithoutInstitutional"),"six-domain ablation infrastructure");
Check(report.Metrics.Any(m=>m.Variable=="Ablation.TrendWithoutAlignment"),"trend subfeature ablation");
Check(report.Correlations.Any(c=>c.Left=="Trend"&&c.Right=="Momentum"),"domain correlation matrix");
Check(report.Correlations.Any(c=>c.Left.StartsWith("Feature.")&&c.Right.StartsWith("Feature.")),"subfeature correlations");
var observation=report.Observations.First();
var expected=Close(1,131)/Close(1,130)-1;
Near(observation.Outcomes.Single(o=>o.Horizon==1).FutureReturn,expected,"independent future close label golden");
Near(ResearchEngine.Value(observation.Trace,"Ablation.WithoutTrend"),observation.Trace.Score.TotalScore!.Value-observation.Trace.Score.Components.Single(c=>c.Code==EvidenceCode.Trend).WeightedPoints!.Value,"ablation subtracts without redistribution");
Check(ResearchMath.Canonical(report)==ResearchMath.Canonical(engine.Run(request)),"exact deterministic replay");
var reordered=request with{Snapshots=request.Snapshots.Reverse().ToImmutableArray(),Outcomes=outcomes with{Series=outcomes.Series.Reverse().ToImmutableArray()}};
Check(ResearchMath.Canonical(report)==ResearchMath.Canonical(engine.Run(reordered)),"canonical input ordering");
var altered=request with{Outcomes=outcomes with{Series=outcomes.Series.Select(s=>s with{Closes=s.Closes.Select(c=>c.Date>=fold.Oos.Start?c with{Close=c.Close*2}:c).ToImmutableArray()}).ToImmutableArray()}};
var alteredReport=engine.Run(altered);
Check(report.Observations.Select(o=>ResearchMath.Canonical(o.Trace)).SequenceEqual(alteredReport.Observations.Select(o=>ResearchMath.Canonical(o.Trace))),"future label cannot enter Candidate input");
Check(ResearchMath.Canonical(report.Metrics)==ResearchMath.Canonical(alteredReport.Metrics),"OOS outcome mutation cannot change development metrics");
var card=ResearchEngine.Scorecard(request,report.Metrics,fold.Code);
Check(card.Digest==ResearchEngine.Scorecard(altered,alteredReport.Metrics,fold.Code).Digest,"OOS mutation cannot change selection identity");
var selection=ResearchProtocolRules.Select(card,At(dates[144]),"Explicit synthetic validation candidate; not production approval.");
var finalRequest=request with{ResearchRunId=Guid.NewGuid(),EvaluateOos=true,Selections=[selection]};
var finalReport=engine.Run(finalRequest);
Check(finalReport.Metrics.Any(m=>m.Partition==ResearchPartition.Oos),"frozen candidate OOS evaluation");
Check(finalReport.PromotionStatus=="RESEARCH_INSUFFICIENT","OOS execution alone is not promotion");
Reject(()=>engine.Run(request with{EvaluateOos=true}),"OOS without selection blocked");
Reject(()=>engine.Run(finalRequest with{Selections=[selection with{SelectedAt=At(fold.Oos.Start)}]}),"selection after OOS starts blocked");
Reject(()=>engine.Run(finalRequest with{Selections=[selection with{ParameterChecksum="changed"}]}),"OOS parameter identity change blocked");
Reject(()=>ResearchProtocolRules.Select(card with{Validation=[report.Metrics[0] with{Partition=ResearchPartition.Oos}]},At(dates[144]),"bad"),"OOS cannot participate in selection");
Reject(()=>ResearchProtocolRules.Select(card,At(dates[130]),"premature"),"selection before label availability blocked");
Check(ResearchProtocolRules.Compare([card]).Length==1,"factual candidate scorecard comparison");
Reject(()=>ResearchProtocolRules.Compare([card,card]),"duplicate parameter comparison blocked");
Reject(()=>ResearchProtocolRules.Validate(protocol with{Folds=[fold with{Validation=new(fold.Train.End,fold.Validation.End)}]},dates),"overlapping chronological split blocked");
Reject(()=>ResearchProtocolRules.Validate(protocol with{EmbargoSessions=3},dates),"insufficient embargo blocked");
Reject(()=>ResearchProtocolRules.Validate(protocol with{Folds=[fold,fold with{Code="F2"}]},dates),"overlapping walk-forward OOS blocked");
var f2=new ResearchFold("F2",new(dates[135],dates[140]),new(dates[143],dates[148]),new(dates[151],dates[154]));
ResearchProtocolRules.Validate(protocol with{Folds=[fold,f2]},dates);Check(true,"chronological walk-forward allows later training on earlier history");
var unavailable=request with{Snapshots=snapshots.Select(s=>s with{Data=s.Data with{Series=s.Data.Series.Select(series=>series with{Flows=series.Flows.Select(f=>f with{AvailableAt=null}).ToImmutableArray()}).ToImmutableArray()}}).ToImmutableArray()};
var missingReport=engine.Run(unavailable);
Check(missingReport.Observations.All(o=>o.Trace.Score.TotalScore is null&&o.Trace.Score.AvailableWeight==80),"missing institutional no 80-to-100 redistribution");
Check(missingReport.ComponentStatus.Single(s=>s.Evidence=="Institutional").Status=="BLOCKED_BY_DATA","institutional blocked by publication evidence");
Check(missingReport.Metrics.Any(m=>m.Variable=="Trend"&&m.AvailableCount>0),"individual Evidence research continues");
var noProof=engine.Run(request with{Snapshots=snapshots.Select(s=>s with{Evidence=s.Evidence with{HistoricalUniverseEvidence=""}}).ToImmutableArray()});
Check(noProof.Status==ResearchRunStatus.Rejected&&noProof.Observations.IsEmpty,"missing historical universe stops run without empty Completed");
var lateUniverse=engine.Run(request with{Snapshots=snapshots.Select(s=>s with{Data=s.Data with{UniverseAvailableAt=At(dates[^1])}}).ToImmutableArray()});
Check(lateUniverse.Observations.All(o=>o.Trace.Score.AvailableWeight==0),"current universe cannot replace historical");
var lateBenchmark=engine.Run(request with{Snapshots=snapshots.Select(s=>s with{Data=s.Data with{BenchmarkMappingAvailableAt=At(dates[^1])}}).ToImmutableArray()});
Check(lateBenchmark.Observations.All(o=>o.Trace.Components.Single(c=>c.Evidence.Code==EvidenceCode.RelativeStrength).Evidence.Value is null),"current benchmark mapping blocked");
var futureFlow=engine.Run(request with{Snapshots=snapshots.Select(s=>s with{Data=s.Data with{Series=s.Data.Series.Select(series=>series with{Flows=series.Flows.Select(f=>f with{AvailableAt=At(dates[^1])}).ToImmutableArray()}).ToImmutableArray()}}).ToImmutableArray()});
Check(futureFlow.Observations.All(o=>o.Trace.Components.Single(c=>c.Evidence.Code==EvidenceCode.Institutional).Evidence.Value is null),"future AvailableAt excluded");
var p2=p with{SlopeHorizon=4};
var changed=request with{Parameters=p2,Snapshots=snapshots.Select(s=>s with{Data=s.Data with{Identity=s.Data.Identity with{ParameterSetId=Guid.Parse("20000000-0000-0000-0000-000000000001"),ParameterChecksum=V3CandidateCalculator.ParameterChecksum(p2)}}}).ToImmutableArray()};
Check(engine.Run(changed).ResearchInputDigest!=report.ResearchInputDigest,"parameter changes research identity");
Check(ResearchMath.Digest(ResearchEngine.CanonicalRequest(request with{CandidateVersion="new-version"}))!=ResearchMath.Digest(ResearchEngine.CanonicalRequest(request)),"candidate version changes input identity");
Reject(()=>engine.Run(request with{CandidateVersion="new-version"}),"unregistered candidate cannot silently replace v1");
Reject(()=>engine.Run(request with{Groups=[new(ids[0],dates[130],"Sector","A","sector-v1",At(dates[^1]))]}),"current sector classification blocked");
var grouped=engine.Run(request with{Groups=snapshots.SelectMany(s=>ids.Select(id=>new ResearchGroup(id,s.Data.Identity.TradeDate,"Regime","Synthetic","regime-fixture-v1",At(s.Data.Identity.TradeDate)))).ToImmutableArray()});
Check(grouped.GroupMetrics.Length>0,"versioned historical group diagnostics");
Check(report.Gaps.Any(g=>g.Code=="MARKETCAP_UNAVAILABLE"),"missing shares does not synthesize market cap");
Reject(()=>ResearchProtocolRules.Transition(ResearchRunStatus.Completed,ResearchRunStatus.Promoted,true),"finalized run immutable and promotion prohibited");
Reject(()=>ResearchProtocolRules.Transition(ResearchRunStatus.Running,ResearchRunStatus.Promoted,false),"no automatic promotion transition");
Check(ResearchProtocolRules.Transition(ResearchRunStatus.Running,ResearchRunStatus.Failed,false)==ResearchRunStatus.Failed,"explicit failed lifecycle");
var temp=Path.Combine(Path.GetTempPath(),"WorkJournalResearchChecks_"+Guid.NewGuid().ToString("N"));
var store=new ResearchStore(temp);var folder=store.Execute(request);
var tinyRows=new[]{
    new ResearchValue(dates[130],ids[0],1,.1m,null),new ResearchValue(dates[130],ids[1],2,.2m,null),new ResearchValue(dates[130],ids[2],3,.3m,null),
    new ResearchValue(dates[131],ids[0],1,.3m,null),new ResearchValue(dates[131],ids[1],2,.2m,null),new ResearchValue(dates[131],ids[2],3,.1m,null)};
var tiny=ResearchMetrics.Calculate("golden",ResearchPartition.Train,"fixture",1,tinyRows,3,0);
Near(tiny.MeanRankIc,0,"daily rank IC averaging golden");Near(tiny.StdDevRankIc,1,"daily rank IC dispersion golden");
Near(tiny.PositiveRankIcRatio,.5m,"positive date IC ratio golden");Near(tiny.TopMinusBottomSpread,0,"top minus bottom golden");
Near(tiny.TurnoverProxy,0,"unchanged top membership turnover golden");
var turnoverRows=tinyRows.Select(r=>r.Date==dates[131]?r with{Value=4-r.Value}:r).ToArray();
Near(ResearchMetrics.Calculate("golden",ResearchPartition.Train,"fixture",1,turnoverRows,3,0).TurnoverProxy,1,"disjoint top membership Jaccard turnover");
var missingOutcome=tinyRows.Take(3).Select(r=>r.InstrumentId==ids[0]?r with{Outcome=null}:r).ToArray();
var missingMetric=ResearchMetrics.Calculate("golden",ResearchPartition.Train,"fixture",1,missingOutcome,3,0);
Check(missingMetric.Quantiles[0].Count==0&&missingMetric.Quantiles[1].Count==1&&missingMetric.Quantiles[2].Count==1,"missing outcome does not reassign evidence quantiles");
Check(missingMetric.OutcomePairCount==2&&missingMetric.AvailableCount==3,"outcome coverage separate from evidence coverage");
var trace=observation.Trace;var anchor=Close(1,130);
Check(ResearchOutcomeCalculator.Calculate(trace,1,outcomes,anchor+1).FutureReturn is null,"future label baseline must match PIT close");
Check(ResearchOutcomeCalculator.Calculate(trace,1,outcomes with{EvaluationCutoff=At(dates[130])},anchor).FutureReturn is null,"unpublished future outcome unavailable");
var alteredBase=finalRequest with{Snapshots=snapshots.Select(s=>s.Data.Identity.TradeDate<fold.Oos.Start?s with{Data=s.Data with{Identity=s.Data.Identity with{DatasetChecksum="changed-reference"}}}:s).ToImmutableArray()};
Reject(()=>engine.Run(alteredBase),"selection binds development data identity not just metrics");
Check(ResearchMath.Canonical(store.Replay(request.ResearchRunId))==ResearchMath.Canonical(report),"durable JSON replay exact");
Reject(()=>store.Execute(request),"finalized run cannot overwrite");
File.AppendAllText(Path.Combine(folder,"quantiles.json")," ");
Reject(()=>store.Replay(request.ResearchRunId),"artifact tampering detected");
Reject(()=>store.Execute(request with{ResearchRunId=Guid.NewGuid(),CandidateVersion="unsupported"}),"failed research persisted separately");
Check(Directory.GetFiles(temp,"failure.json",SearchOption.AllDirectories).Length==1,"failure artifact retained");
// Deliberately retain uniquely named temporary audit directory; never delete unrelated files.
Console.WriteLine("Research persistence fixture: "+temp);
if(args.Length==2&&args[0]=="--export-fixture")
{
    Directory.CreateDirectory(args[1]);
    File.WriteAllText(Path.Combine(args[1],"synthetic-research-request.json"),JsonSerializer.Serialize(ResearchEngine.CanonicalRequest(request)));
    File.WriteAllText(Path.Combine(args[1],"synthetic-oos-request.json"),JsonSerializer.Serialize(ResearchEngine.CanonicalRequest(finalRequest)));
    File.WriteAllText(Path.Combine(args[1],"synthetic-scorecard.json"),ResearchMath.Canonical(card));
}
Console.WriteLine($"V3 Research checks passed: {checks}; synthetic infrastructure only, no model promotion.");
