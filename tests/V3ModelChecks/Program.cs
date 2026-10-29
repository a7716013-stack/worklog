using System.Collections.Immutable;
using System.Text.Json;
using WorkJournal.Web.Models.Evidence;
using WorkJournal.Web.Models.V2;
using WorkJournal.Web.Services.Evidence;
int count = 0;
void Check(bool condition,string label) { if (!condition) throw new Exception(label); count++; Console.WriteLine("PASS: "+label); }
void Near(decimal actual,decimal expected,string label,decimal tolerance = .000000000001m) => Check(Math.Abs(actual-expected) <= tolerance,label+" actual="+actual);
void Reject(Action action,string label) { try { action(); } catch (ArgumentException) { Check(true,label); return; } throw new Exception("Expected rejection: "+label); }
var bounds = new LinearBounds(-1,1);
foreach (var (x,y) in new[] { (-2m,0m),(-1m,0m),(0m,.5m),(1m,1m),(2m,1m) })
{ Near(CandidateMath.Linear(x,bounds),y,"linear boundary "+x); Near(CandidateMath.InverseLinear(x,bounds),1-y,"inverse boundary "+x); }
Reject(() => CandidateMath.Linear(0,new(1,1)),"degenerate bounds");
Reject(() => CandidateMath.Percentile(0,Array.Empty<decimal>()),"empty percentile");
Near(CandidateMath.Percentile(2,new decimal[] {1,2,2,3}),.5m,"midrank ties");
Near(CandidateMath.Percentile(7,new decimal[] {7}),.5m,"singleton document formula");
Near(CandidateMath.Log(1),0,"ln one");
Near(CandidateMath.Log(2),.6931471805599453094m,"ln two");
Near(CandidateMath.Log(.5m),-.6931471805599453094m,"ln half");
Reject(() => CandidateMath.Log(0),"zero log rejected");
Near(CandidateMath.Sqrt(4),2,"sqrt four");
Near(CandidateMath.Sqrt(.0001m),.01m,"sqrt small");
Near(CandidateMath.Median(new decimal[] {4,1,3,2}),2.5m,"even median");
Near(CandidateMath.Median(new decimal[] {5,1,2}),2,"odd median");
var emptyParameters = new V3EvidenceCandidateV1ParameterSet();
foreach (var anchor in emptyParameters.RsiAnchors) Near(CandidateMath.Piecewise(anchor.X,emptyParameters.RsiAnchors),anchor.Y,"RSI anchor "+anchor.X);
Near(CandidateMath.Piecewise(55,emptyParameters.RsiAnchors),.625m,"RSI interpolation");
Near(CandidateMath.Piecewise(0,emptyParameters.RsiAnchors),.1m,"RSI lower tail");
Near(CandidateMath.Piecewise(100,emptyParameters.RsiAnchors),.4m,"RSI upper tail");
Reject(() => CandidateMath.Piecewise(1,new Anchor[] {new(1,0),new(1,1)}),"duplicate anchors");
// Synthetic test-only parameters, not a calibrated/default research preset.
var p = emptyParameters with {
    SlopeHorizon=5,StructureHorizon=10,AlignmentBounds=bounds,PositionBounds=bounds,SlopeBounds=bounds,
    RocHorizon=5,MacdBounds=bounds,RocBounds=bounds,VolumeHorizon=5,ReturnHorizon=5,BreakoutHorizon=5,
    RvolLogBounds=bounds,PvcBounds=bounds,BreakoutBounds=bounds,
    InstitutionalHorizons=[2,3,5],PersistenceHorizon=5,Persistence=PersistenceGroup.Combined,
    RelativeStrengthHorizons=[2,3,5],VolatilityHorizon=5,Variance=VarianceDefinition.Population,
    DrawdownHorizon=5,AtrPeriod=5,Atr=AtrDefinition.SimpleMean,LiquidityHorizon=5,LiquidityPrice="Close"
};
var stock = Guid.Parse("10000000-0000-0000-0000-000000000001");
var peer = Guid.Parse("10000000-0000-0000-0000-000000000002");
var first = new DateOnly(2020,1,1);
var sessions = Enumerable.Range(0,140).Select(i => first.AddDays(i)).ToImmutableArray();
var date = sessions[^1]; var cutoff = new DateTimeOffset(date.ToDateTime(new TimeOnly(23,0)),TimeSpan.Zero);
var bars = sessions.Select(d => new CandidateBar(d,100,101,99,1000,new DateTimeOffset(d.ToDateTime(new TimeOnly(16,0)),TimeSpan.Zero))).ToImmutableArray();
var flows = bars.Select(b => new CandidateFlow(b.Date,10,5,1,b.AvailableAt)).ToImmutableArray();
var identity = new CandidateIdentity(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),
    V3CandidateCalculator.ParameterChecksum(p),"fixture-dataset","fixture-universe","fixture-benchmark","unadjusted-fixture-v1",date,cutoff);
var data = new CandidateDataset(identity,FoundationStatus.Finalized,cutoff.AddHours(-1),cutoff.AddDays(-150),sessions,[stock,peer],
    [new(stock,bars,flows),new(peer,bars,flows)],bars);
var calc = new V3CandidateCalculator();
CandidateTrace Run(CandidateDataset? d = null,V3EvidenceCandidateV1ParameterSet? parameters = null) => calc.Calculate(d ?? data,stock,parameters ?? p);
CandidateDataset Param(V3EvidenceCandidateV1ParameterSet parameters) => data with { Identity = identity with { ParameterChecksum=V3CandidateCalculator.ParameterChecksum(parameters) } };
CandidateDataset StockBars(ImmutableArray<CandidateBar> b) => data with {Series=data.Series.SetItem(0,data.Series[0] with {Bars=b})};
CandidateDataset StockFlows(ImmutableArray<CandidateFlow> f) => data with {Series=data.Series.SetItem(0,data.Series[0] with {Flows=f})};
CandidateComponent Component(CandidateTrace result,EvidenceCode code) => result.Components.Single(c => c.Evidence.Code==code);
void Unavailable(CandidateTrace result,EvidenceCode code,string label,EvidenceStatus? status=null)
{
    var e=Component(result,code).Evidence;
    Check(e.Value is null && e.Status != EvidenceStatus.Available && (status is null || e.Status==status),label);
    Check(result.Score.TotalScore is null,label+" blocks total");
}
var golden = Run();
Check(golden.Score.Status==EvidenceScoreStatus.Complete,"synthetic complete candidate");
Near(Component(golden,EvidenceCode.Trend).Evidence.Value!.Value,.5m,"flat Trend golden");
Near(Component(golden,EvidenceCode.Momentum).Evidence.Value!.Value,.5m,"flat Momentum golden");
var vpGolden = .4m*.5m+.35m*.5m+.25m*((-1m/101+1)/2);
Near(Component(golden,EvidenceCode.VolumePrice).Evidence.Value!.Value,vpGolden,"VolumePrice independent golden");
Near(Component(golden,EvidenceCode.Institutional).Evidence.Value!.Value,.6m,"tied flows plus positive persistence");
Near(Component(golden,EvidenceCode.RelativeStrength).Evidence.Value!.Value,.5m,"RS tied population");
Near(Component(golden,EvidenceCode.RiskQuality).Evidence.Value!.Value,.5m,"risk tied population");
Near(golden.Score.TotalScore!.Value,25*.5m+15*.5m+15*vpGolden+20*.6m+15*.5m+10*.5m,"aggregate golden");
Check(golden.ValidationStatus.Contains("NOT_PRODUCTION"),"candidate cannot claim production verification");
Check(JsonSerializer.Serialize(golden)==JsonSerializer.Serialize(Run()),"deterministic replay byte equality");
var reversed = data with {Series=data.Series.Reverse().Select(s=>s with {Bars=s.Bars.Reverse().ToImmutableArray(),Flows=s.Flows.Reverse().ToImmutableArray()}).ToImmutableArray(),UniverseMembers=data.UniverseMembers.Reverse().ToImmutableArray(),Sessions=data.Sessions.Reverse().ToImmutableArray(),Benchmark=data.Benchmark.Reverse().ToImmutableArray()};
Check(JsonSerializer.Serialize(golden)==JsonSerializer.Serialize(Run(reversed)),"input ordering independent replay");
var futureBar = bars[^1] with {Date=date.AddDays(1),Close=999,High=1000,AvailableAt=cutoff.AddDays(1)};
var future = data with {Series=data.Series.SetItem(0,data.Series[0] with {Bars=bars.Add(futureBar),Flows=flows.Add(flows[^1] with {Date=date.AddDays(1),AvailableAt=cutoff.AddDays(1)})}),Sessions=sessions.Add(date.AddDays(1)),Benchmark=bars.Add(futureBar)};
Check(JsonSerializer.Serialize(golden)==JsonSerializer.Serialize(Run(future)),"future data cannot change historical trace");
var correction = StockBars(bars.Add(bars[^1] with {Close=500,High=501,AvailableAt=cutoff.AddDays(1)}));
Check(JsonSerializer.Serialize(golden)==JsonSerializer.Serialize(Run(correction)),"future revision cannot change trace");
Unavailable(Run(StockFlows(flows.SetItem(flows.Length-1,flows[^1] with {AvailableAt=null}))),EvidenceCode.Institutional,"trade date is not availability");
Unavailable(Run(StockFlows(flows.SetItem(flows.Length-1,flows[^1] with {AvailableAt=cutoff.AddMinutes(1)}))),EvidenceCode.Institutional,"future institutional publication");
Unavailable(Run(data with {BenchmarkMappingAvailableAt=null}),EvidenceCode.RelativeStrength,"mapping knowledge time required");
Unavailable(Run(data with {Benchmark=bars.SetItem(bars.Length-1,bars[^1] with {Status=EvidenceStatus.Invalid})}),EvidenceCode.RelativeStrength,"invalid benchmark",EvidenceStatus.Invalid);
Unavailable(Run(StockBars(bars.SetItem(bars.Length-1,bars[^1] with {Status=EvidenceStatus.Stale}))),EvidenceCode.Trend,"stale data",EvidenceStatus.Stale);
Unavailable(Run(StockBars(bars.RemoveAt(bars.Length-2))),EvidenceCode.Momentum,"missing session cannot be bridged");
Unavailable(Run(StockBars(bars.Add(bars[^1]))),EvidenceCode.Trend,"duplicate session",EvidenceStatus.Invalid);
Unavailable(Run(StockBars(bars.SetItem(bars.Length-1,bars[^1] with {Close=0}))),EvidenceCode.Trend,"invalid close",EvidenceStatus.Invalid);
Unavailable(Run(StockBars(bars.SetItem(bars.Length-1,bars[^1] with {Volume=0}))),EvidenceCode.VolumePrice,"zero volume log",EvidenceStatus.Invalid);
Unavailable(Run(StockBars(bars.Select(b=>b with {High=100,Low=100}).ToImmutableArray())),EvidenceCode.Trend,"zero structure range",EvidenceStatus.Invalid);
var noPeer = data with {Series=data.Series.RemoveAt(1)};
foreach (var code in new[] {EvidenceCode.Institutional,EvidenceCode.RelativeStrength,EvidenceCode.RiskQuality}) Unavailable(Run(noPeer),code,"incomplete cohort "+code);
Check(Component(Run(noPeer),EvidenceCode.Trend).Evidence.Status==EvidenceStatus.Available,"unrelated evidence retains availability");
Check(Run(data with {UniverseAvailableAt=null}).Score.AvailableWeight==0,"unverifiable universe blocks all components");
var gaps = Run(Param(emptyParameters),emptyParameters);
Check(gaps.Score.AvailableWeight==0 && gaps.Components.All(c=>c.Evidence.ReasonCode.StartsWith("MODEL_DEFINITION_GAP")),"no guessed default parameters");
var sample = p with {Variance=VarianceDefinition.Sample};
Check(Run(Param(sample),sample).CalculationId!=golden.CalculationId,"parameter changes identity");
Reject(()=>Run(data,sample),"parameter checksum mismatch");
Reject(()=>Run(data,p with {TrendWeights=[.4m,.2m,.2m,.2m]}),"silent candidate formula change rejected");
Reject(()=>Run(data with {Status=FoundationStatus.Draft}),"draft references rejected");
Reject(()=>Run(data with {UniverseMembers=[stock,stock]}),"duplicate universe rejected");
Reject(()=>Run(data with {UniverseMembers=[peer]}),"unrelated universe rejected");
var badBounds=p with {AlignmentBounds=new(1,0)};
Unavailable(Run(Param(badBounds),badBounds),EvidenceCode.Trend,"invalid normalization bounds",EvidenceStatus.Invalid);
var bumped=bars.SetItem(bars.Length-1,bars[^1] with {Close=102,High=103,Volume=2000});
var bumpedResult=Run(StockBars(bumped));
var t=Component(bumpedResult,EvidenceCode.Trend);
Near(t.Features.Single(f=>f.Code=="Trend.Alignment").RawValue,((100.1m-(100+2m/60))/(100+2m/60)+((100+2m/60)-(100+2m/120))/(100+2m/120))/2,"SMA average definition");
Near(t.Features.Single(f=>f.Code=="Trend.Structure").NormalizedValue,1,"prior range excludes current high");
Near(Component(bumpedResult,EvidenceCode.VolumePrice).Features.Single(f=>f.Code=="VolumePrice.RVOLLog").RawValue,CandidateMath.Log(2),"prior volume excludes current volume");
Near(Component(bumpedResult,EvidenceCode.Momentum).Features.Single(f=>f.Code=="Momentum.ROC").RawValue,.02m,"ROC exact lag");
Near(Component(bumpedResult,EvidenceCode.RelativeStrength).Evidence.Value!.Value,.75m,"historical two-member upper rank");
var risk=Component(bumpedResult,EvidenceCode.RiskQuality);
Near(risk.Features.Single(f=>f.Code=="Risk.Volatility").RawValue,.008m,"population standard deviation");
Near(risk.Features.Single(f=>f.Code=="Risk.ATR").RawValue,2.4m/102,"simple ATR explicit period");
Near(risk.Features.Single(f=>f.Code=="Risk.Liquidity").RawValue,100000,"median liquidity");
var sampleBump=p with {Variance=VarianceDefinition.Sample};
var sampleResult=Run(StockBars(bumped) with {Identity=identity with {ParameterChecksum=V3CandidateCalculator.ParameterChecksum(sampleBump)}},sampleBump);
Near(Component(sampleResult,EvidenceCode.RiskQuality).Features.Single(f=>f.Code=="Risk.Volatility").RawValue,CandidateMath.Sqrt(.00008m),"sample variance denominator");
var wilder=p with {Atr=AtrDefinition.WilderSeededMean};
var wilderResult=Run(StockBars(bumped) with {Identity=identity with {ParameterChecksum=V3CandidateCalculator.ParameterChecksum(wilder)}},wilder);
Near(Component(wilderResult,EvidenceCode.RiskQuality).Features.Single(f=>f.Code=="Risk.ATR").RawValue,2.4m/102,"Wilder seed and recurrence");
var momentum=Component(bumpedResult,EvidenceCode.Momentum);
Near(momentum.Features.Single(f=>f.Code=="Momentum.MACD").RawValue,2*(2m/13-2m/27)*.8m/102,"MACD independent EMA impulse golden");
Near(momentum.Features.Single(f=>f.Code=="Momentum.RSI").NormalizedValue,.4m,"Wilder RSI100 overbought decline");
var down=bars.SetItem(bars.Length-1,bars[^1] with {Close=98,Low=97});
Near(Component(Run(StockBars(down)),EvidenceCode.RiskQuality).Features.Single(f=>f.Code=="Risk.Drawdown").RawValue,.02m,"drawdown includes historical peak");
var variedFlow=Run(StockFlows(flows.SetItem(flows.Length-1,flows[^1] with {Foreign=110,Trust=-95,Dealer=101})));
var institution=Component(variedFlow,EvidenceCode.Institutional);
Near(institution.Features.Single(f=>f.Code=="Institutional.Foreign").RawValue,.25m*.06m+.35m*(130m/3000)+.4m*.03m,"sum-flow/sum-volume then horizon weighting");
Near(institution.Evidence.Value!.Value,.65m,"independent institutional rank weighting");
Check(CandidateFeatureCatalog.Create(identity.ParameterSetId).Length==19,"19 versioned feature definitions");
Check(CandidateFeatureCatalog.Create(identity.ParameterSetId).All(f=>f.Status==DefinitionStatus.Candidate && f.Normalization!.ParameterSetId==identity.ParameterSetId),"definitions remain Candidate and bind parameters");
Check(Run(StockBars(bumped)).CalculationId!=golden.CalculationId,"input digest prevents same identity silently reusing changed payload");
var missingParameter=p with {LiquidityPrice=null};
Unavailable(Run(Param(missingParameter),missingParameter),EvidenceCode.RiskQuality,"liquidity price proxy must be explicit");
Reject(()=>Run(data with {Series=default}),"omitted array rejected");
// Optional reproducible offline demonstration, never imported as production data.
if (args.Length==2 && args[0]=="--export-fixture")
{
    Directory.CreateDirectory(args[1]);
    var options=new JsonSerializerOptions {WriteIndented=true};
    File.WriteAllText(Path.Combine(args[1],"synthetic-dataset.json"),JsonSerializer.Serialize(data,options));
    File.WriteAllText(Path.Combine(args[1],"synthetic-parameters.json"),JsonSerializer.Serialize(p,options));
    File.WriteAllText(Path.Combine(args[1],"synthetic-expected-trace.json"),JsonSerializer.Serialize(golden,options));
    File.WriteAllText(Path.Combine(args[1],"unconfigured-parameters.json"),JsonSerializer.Serialize(emptyParameters,options));
}
Console.WriteLine($"V3 Candidate model checks passed: {count}. Synthetic fixtures only; Research/OOS not performed.");
