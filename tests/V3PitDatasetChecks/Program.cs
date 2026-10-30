using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WorkJournal.Web.Models.Evidence;
using WorkJournal.Web.Models.HistoricalResearch;
using WorkJournal.Web.Models.Research;
using WorkJournal.Web.Models.V2;
using WorkJournal.Web.Services.Evidence;
using WorkJournal.Web.Services.HistoricalResearch;
using WorkJournal.Web.Services.Research;

int count=0;
void Check(bool condition,string name){if(!condition)throw new Exception(name);Console.WriteLine("PASS: "+name);count++;}
void Reject(Action action,string name){try{action();}catch(Exception e) when(e is ArgumentException or IOException or InvalidOperationException or InvalidDataException){Check(true,name);return;}throw new Exception("Expected rejection: "+name);}
var start=new DateOnly(2024,1,1);var dates=Enumerable.Range(0,160).Select(start.AddDays).ToImmutableArray(); // EXPLICIT synthetic calendar, not a real exchange calendar.
DateTimeOffset At(DateOnly d)=>new(d.ToDateTime(new TimeOnly(18,0)),TimeSpan.Zero);
var created=At(dates[^1]).AddDays(1);var published=At(dates[0]).AddDays(-1);
Guid Id(int n)=>Guid.Parse($"00000000-0000-0000-0000-{n:000000000000}");
var ids=new[]{Id(1),Id(2),Id(3)};var bmId=Id(4);
var p=new V3EvidenceCandidateV1ParameterSet{SlopeHorizon=5,StructureHorizon=10,AlignmentBounds=new(-.1m,.1m),PositionBounds=new(-.1m,.1m),SlopeBounds=new(-.1m,.1m),
    RocHorizon=5,MacdBounds=new(-.01m,.01m),RocBounds=new(-.1m,.1m),VolumeHorizon=5,ReturnHorizon=5,BreakoutHorizon=5,
    RvolLogBounds=new(-1,1),PvcBounds=new(-.1m,.1m),BreakoutBounds=new(-.1m,.1m),InstitutionalHorizons=[2,3,5],PersistenceHorizon=5,Persistence=PersistenceGroup.Combined,
    RelativeStrengthHorizons=[2,3,5],VolatilityHorizon=5,Variance=VarianceDefinition.Population,DrawdownHorizon=5,AtrPeriod=5,Atr=AtrDefinition.SimpleMean,LiquidityHorizon=5,LiquidityPrice="Close"};
var types=Enum.GetValues<HistoricalDatasetType>();
var archives=types.Select(t=>
{
    var bytes=Encoding.UTF8.GetBytes("EXPLICIT SYNTHETIC SOURCE "+t);
    return new SourceArchive(t.ToString(),"fixture",t,"fixture://"+t,created,created,Convert.ToBase64String(bytes),Convert.ToHexString(SHA256.HashData(bytes)),"fixture-v1",true);
}).ToImmutableArray();
var policies=types.Select(t=>new FrozenAvailabilityPolicy(Id(20+(int)t),"fixture",t,"fixture-v1",start,null,AvailabilityRule.SourceTimestamp,"UTC",null,
    "fixture://publication","Explicit synthetic publication/revision contract","fixture-review",published,published,FoundationStatus.Finalized)).ToImmutableArray();
var facts=new List<HistoricalFact>();
HistoricalFact Seal(HistoricalFact f)=>f with{FactChecksum=HistoricalAvailability.FactChecksum(f)};
foreach(var type in types)foreach(var id in type==HistoricalDatasetType.Benchmark?new[]{bmId}:ids)
foreach(var (date,i) in dates.Select((d,i)=>(d,i)))
{
    var n=Array.IndexOf(ids,id)+1;var close=100+i*.1m*(n+1)+(i%3)*.1m;var a=archives.Single(a=>a.DatasetType==type);
    var value=type==HistoricalDatasetType.Institutional?new HistoricalValue(Foreign:n*10+i%2,Trust:5-n,Dealer:1):new(close,close+1,close-1,close,1000+i,100000+i);
    facts.Add(Seal(new("fixture",type,id.ToString(),date,At(date).AddHours(-18),At(date),created,created,"revision-1",$"{type}/{id}/{date}",a.ArchiveId,value,
        "Shares","TWD",EvidenceStatus.Available,a.SourceChecksum,"",Id(20+(int)type),AvailabilityConfidence.Known,"fixture publication","fixture original revision","fixture-raw-v1")));
}
var researchDates=dates.Skip(130).Take(21).Select(d=>new HistoricalResearchDate(d,At(d).AddHours(1))).ToImmutableArray();
var universes=researchDates.Select((d,i)=>
{
    var u=new HistoricalUniverse(Id(100+i),"fixture-historical-v1",d.TradeDate,published,ids.ToImmutableArray(),"synthetic membership","fixture://historical-listing",true,"");
    return u with{Checksum=HistoricalAvailability.UniverseChecksum(u)};
}).ToImmutableArray();
var request=new HistoricalBuildRequest("fixture-dataset-v1","pit-builder-v1",created,Id(10),Id(11),V3CandidateCalculator.ParameterChecksum(p),
    new("fixture-calendar-v1",dates,published,"fixture://exchange-calendar"),new("fixture-raw-v1","Raw","fixture-actions-v1","fixture://corporate-actions","fixture-review",true),
    researchDates,archives,facts.ToImmutableArray(),policies,ids.Append(bmId).Select(id=>new HistoricalInstrumentMapping("fixture",id.ToString(),id,start,null,published,"fixture://identity","fixture-v1")).ToImmutableArray(),
    universes,ids.Select(id=>new HistoricalBenchmarkMapping(id,bmId,Id(12),start,null,published,"fixture-v1","fixture://historical-benchmark")).ToImmutableArray());
var builder=new HistoricalDatasetBuilder();var result=builder.Build(request);
Check(result.Snapshots.Length==21,"historical snapshots built");
Check(result.Quality.Coverage.All(c=>c.Coverage==1),"known universe/calendar denominators complete");
Check(result.Snapshots.All(s=>s.Evidence.Synthetic),"synthetic sources remain explicitly synthetic");
var calculator=new V3CandidateCalculator();var trace=calculator.Calculate(result.Snapshots[0].Data,ids[0],p);
Check(trace.Score.AvailableWeight==100&&trace.Score.TotalScore.HasValue,"existing shared candidate receives complete fixture");
var reordered=builder.Build(request with{Facts=request.Facts.Reverse().ToImmutableArray(),Archives=request.Archives.Reverse().ToImmutableArray(),Universes=request.Universes.Reverse().ToImmutableArray(),Instruments=request.Instruments.Reverse().ToImmutableArray(),Benchmarks=request.Benchmarks.Reverse().ToImmutableArray(),Policies=request.Policies.Reverse().ToImmutableArray(),Dates=request.Dates.Reverse().ToImmutableArray()});
Check(result.DatasetChecksum==reordered.DatasetChecksum,"dataset checksum independent of input set order");
Check(result.Snapshots[0].Data.Identity.BenchmarkChecksum==reordered.Snapshots[0].Data.Identity.BenchmarkChecksum,"benchmark identity deterministic");
Check(HistoricalAvailability.UniverseChecksum(universes[0])==HistoricalAvailability.UniverseChecksum(universes[0] with{Members=universes[0].Members.Reverse().ToImmutableArray()}),"universe checksum independent of member order");
var missing=builder.Build(request with{Facts=request.Facts.Select(f=>f.DatasetType==HistoricalDatasetType.Institutional?Seal(f with{AvailableAt=null,Confidence=AvailabilityConfidence.Unknown}):f).ToImmutableArray()});
var partial=calculator.Calculate(missing.Snapshots[0].Data,ids[0],p);
Check(missing.Snapshots.All(s=>s.Data.Series.All(x=>x.Flows.Length==0)),"unknown institutional availability never becomes zero flow");
Check(partial.Score.AvailableWeight==80&&partial.Score.TotalScore is null,"80 available points never renormalize to 100");
Check(partial.Components.Single(c=>c.Evidence.Code==EvidenceCode.Trend).Evidence.Value==trace.Components.Single(c=>c.Evidence.Code==EvidenceCode.Trend).Evidence.Value,"independent component survives missing institutional facts");
var first=request.Facts[0];
Check(HistoricalAvailability.Resolve(first with{Confidence=AvailabilityConfidence.Unknown},policies[0]).At is null,"unknown timestamp fails closed");
Check(HistoricalAvailability.Resolve(first,policies[0] with{Status=FoundationStatus.Draft}).At is null,"unfrozen policy fails closed");
Check(HistoricalAvailability.Resolve(first with{RevisionEvidence=""},policies[0]).At is null,"revision proof required");
var derived=first with{Confidence=AvailabilityConfidence.DerivedFromVerifiedRule,AvailableAt=null};
Check(HistoricalAvailability.Resolve(derived,policies[0] with{Rule=AvailabilityRule.VerifiedLocalPublicationTime,LocalPublicationTime=new(18,0)}).At==first.AvailableAt,"reviewed timezone publication rule derives exact time");
Check(HistoricalAvailability.Resolve(derived,policies[0] with{Rule=AvailabilityRule.VerifiedLocalPublicationTime,LocalPublicationTime=null}).At is null,"no default publication hour");
var future=Seal(first with{RevisionId="future-revision",AvailableAt=created,Value=first.Value with{Close=100.5m}});
var withFuture=builder.Build(request with{Facts=request.Facts.Add(future)});
Check(withFuture.Decisions.Where(d=>d.FactChecksum==future.FactChecksum).All(d=>!d.Eligible),"future source revision blocked");
Check(withFuture.Snapshots[0].Data.Series[0].Bars[0].Close==result.Snapshots[0].Data.Series[0].Bars[0].Close,"future revision cannot change earlier feature values");
Check(withFuture.DatasetSnapshotId!=result.DatasetSnapshotId,"different source revision changes archive identity");
var duplicate=builder.Build(request with{Facts=request.Facts.Add(first)});
Check(duplicate.Quality.DuplicateRecords==2&&!duplicate.Decisions.Any(d=>d.FactChecksum==first.FactChecksum&&d.Eligible),"duplicate source revision rejected without silent deduplication");
var tie=Seal(first with{RevisionId="conflicting-same-time",Value=first.Value with{Close=100.5m}});
Check(builder.Build(request with{Facts=request.Facts.Add(tie)}).Gaps.Any(g=>g.Detail.Contains("Ambiguous revision")),"conflicting equal-time revisions blocked");
var currentUniverse=builder.Build(request with{Universes=request.Universes.Select(u=>u with{AvailableAt=created}).ToImmutableArray()});
Check(currentUniverse.Snapshots.Length==0&&currentUniverse.Gaps.Any(g=>g.Code=="UNIVERSE_GAP"),"current universe cannot be backfilled into history");
var culture=System.Globalization.CultureInfo.CurrentCulture;
try
{
    System.Globalization.CultureInfo.CurrentCulture=System.Globalization.CultureInfo.GetCultureInfo("en-US");
    var english=builder.Build(request with{Universes=[]});
    System.Globalization.CultureInfo.CurrentCulture=System.Globalization.CultureInfo.GetCultureInfo("zh-TW");
    Check(english.DatasetChecksum==builder.Build(request with{Universes=[]}).DatasetChecksum,"dataset and diagnostic checksum independent of locale");
}
finally{System.Globalization.CultureInfo.CurrentCulture=culture;}
var currentBenchmark=builder.Build(request with{Benchmarks=request.Benchmarks.Select(b=>b with{AvailableAt=created}).ToImmutableArray()});
Check(currentBenchmark.Snapshots.All(s=>s.Data.Benchmark.Length==0&&s.Data.BenchmarkMappingAvailableAt is null),"current benchmark mapping cannot feed relative strength");
Check(builder.Build(request with{PricePolicy=request.PricePolicy with{Frozen=false}}).Snapshots.Length==0,"unreviewed corporate actions block dataset");
var basis=builder.Build(request with{Facts=request.Facts.Select(f=>f.DatasetType==HistoricalDatasetType.Price?Seal(f with{PriceBasisVersion="other"}):f).ToImmutableArray()});
Check(basis.Snapshots.All(s=>s.Data.Series.All(x=>x.Bars.Length==0)),"mixed price basis blocked");
var tamper=builder.Build(request with{Archives=request.Archives.SetItem(0,archives[0] with{BodyBase64=Convert.ToBase64String(Encoding.UTF8.GetBytes("tampered"))})});
Check(tamper.Decisions.Where(d=>request.Facts.Where(f=>f.DatasetType==HistoricalDatasetType.Price).Select(f=>f.FactChecksum).Contains(d.FactChecksum)).All(d=>!d.Eligible),"raw archive checksum tamper blocked");
Check(builder.Build(request with{Universes=request.Universes.Select(u=>u with{IncludesDelistedHistory=false,Checksum=HistoricalAvailability.UniverseChecksum(u with{IncludesDelistedHistory=false})}).ToImmutableArray()}).Gaps.Any(g=>g.Code=="SURVIVORSHIP_BIAS_RISK"),"delisted coverage limitation exposed");
var directory=Path.Combine(Path.GetTempPath(),"workjournal-pit-"+Guid.NewGuid().ToString("N"));var store=new HistoricalDatasetStore(directory);
var saved=store.Execute(request);
Check(store.Replay(saved.DatasetSnapshotId).DatasetChecksum==saved.DatasetChecksum,"durable dataset replay identical");
var manifestPath=Path.Combine(directory,saved.DatasetSnapshotId,"manifest.json");var manifestText=File.ReadAllText(manifestPath);
File.WriteAllText(manifestPath,manifestText.Replace("fixture-actions-v1","tampered-actions"));
Reject(()=>store.Replay(saved.DatasetSnapshotId),"manifest policy metadata tampering rejected");
File.WriteAllText(manifestPath,manifestText);
Reject(()=>store.Execute(request),"finalized dataset cannot be overwritten");
Reject(()=>store.Replay("../outside"),"snapshot path traversal rejected");
File.AppendAllText(Path.Combine(directory,saved.DatasetSnapshotId,"result.json"),"x");
try{store.Replay(saved.DatasetSnapshotId);throw new Exception("tamper accepted");}catch(JsonException){Check(true,"tampered persisted result fails replay");}
var registry=new ResearchCandidateRegistry(Path.Combine(directory,"registry"));
var candidate=new ResearchCandidateRegistration(V3CandidateCalculator.Version,"All",["CandidateFeatures-v1"],["CandidateNormalization-v1"],request.ParameterSetId,request.ParameterChecksum,created,null,"CANDIDATE","Synthetic checks only");
registry.Register(candidate);Reject(()=>registry.Register(candidate with{Reason="silent overwrite"}),"candidate v1 cannot be overwritten");
registry.Register(candidate with{CandidateVersion="failed-fixture-v2",Status="REJECTED"});
Check(registry.ReadAll().Length==2&&registry.ReadAll().Any(c=>c.Status=="REJECTED"),"failed candidate retained durably");
Reject(()=>registry.Register(candidate with{CandidateVersion="promoted",Status="VALIDATED"}),"registry cannot automatically validate model");
var outcomes=new OutcomeDataset("fixture-labels","fixture-v1",created,dates,ids.Select(id=>new OutcomeSeries(id,"fixture-raw-v1",request.Facts.Where(f=>f.SourceInstrumentKey==id.ToString()&&f.DatasetType==HistoricalDatasetType.Price).Select(f=>new OutcomeClose(f.TradeDate,f.Value.Close!.Value,f.AvailableAt,EvidenceStatus.Available)).ToImmutableArray())).ToImmutableArray());
var protocol=new ResearchProtocol("fixture-v1",[new("F1",new(dates[130],dates[135]),new(dates[138],dates[143]),new(dates[146],dates[150]))],[1,2],3,2);
var research=new ResearchRequest(Id(99),created,V3CandidateCalculator.Version,"fixture-model-v1","CandidateFeatures-v1",p,protocol,result.Snapshots,outcomes,[],[]);
var report=new ResearchEngine().Run(research);
Check(report.Status==ResearchRunStatus.Completed,"PIT adapter drives existing research engine");
Check(report.Metrics.All(m=>m.Partition!=ResearchPartition.Oos),"OOS absent from development metrics");
var changedLabels=new ResearchEngine().Run(research with{Outcomes=outcomes with{Series=outcomes.Series.Select(s=>s with{Closes=s.Closes.Select(c=>c with{Close=c.Close*2}).ToImmutableArray()}).ToImmutableArray()}});
Check(report.Observations.Select(o=>o.Trace.InputDigest).SequenceEqual(changedLabels.Observations.Select(o=>o.Trace.InputDigest)),"future labels never enter candidate input");
var summary=EvidenceValidationReport.Create(research,report,registry.ReadAll(),[report]);
Check(summary.Metrics.Any(m=>m.MedianIc.HasValue&&m.IcStdDev.HasValue&&m.PositiveIcRatio.HasValue),"IC median dispersion and positive ratio recorded");
Check(summary.YearDistributions.Length>0&&summary.Correlations.Length>0,"year distributions and correlations retained");
Check(summary.Evidence.Length==6&&summary.Evidence.All(e=>e.FormulaStatus=="IMPLEMENTED"&&e.PromotionDecision=="RESEARCH_INSUFFICIENT"),"formula/data/research/OOS statuses separated");
Check(summary.ThresholdStatus=="UNRESOLVED"&&summary.NumberOfCandidatesRegistered==2&&summary.NumberOfCandidatesTested==1,"multiple testing counts and threshold lock");
Check(summary.Gaps.Any(g=>g.Code=="CLASSIFICATION_GAP"),"historical classification gaps explicit");
Reject(()=>EvidenceValidationReport.Create(research,report with{ResearchOutputDigest="fake"},registry.ReadAll(),[]),"orphan report metrics rejected");
// Captured official-shaped payload is still UNKNOWN, even when its TradeDate is known.
var payload=JsonSerializer.Serialize(new{stat="OK",date="20240101",fields=new[]{"日期","成交股數","成交金額","開盤價","最高價","最低價","收盤價","漲跌價差"},data=new[]{new[]{"113/01/02","1,000","100,000","100","101","99","100","0"}}});
var raw=Encoding.UTF8.GetBytes(payload);var archive=new SourceArchive("capture-fixture","TWSE",HistoricalDatasetType.Price,"https://www.twse.com.tw/rwd/zh/afterTrading/STOCK_DAY",created,created,Convert.ToBase64String(raw),Convert.ToHexString(SHA256.HashData(raw)),"TWSE-capture-v1",true);
var capture=OfficialHistoricalSource.Parse(archive,start,"2330");
Check(capture.Facts.Length==1&&capture.Facts[0].Value.Volume==1000&&capture.Facts[0].AvailableAt is null&&capture.Facts[0].Confidence==AvailabilityConfidence.Unknown,"official-shaped capture retains unknown availability and exact units");
if(args.Length==2&&args[0]=="--export-fixture")
{
    Directory.CreateDirectory(args[1]);HistoricalDatasetStore.WriteNew(Path.Combine(args[1],"synthetic-pit-request.json"),request);
    HistoricalDatasetStore.WriteNew(Path.Combine(args[1],"synthetic-research-template.json"),research with{Snapshots=[],Selections=[]});
    HistoricalDatasetStore.WriteNew(Path.Combine(args[1],"synthetic-candidate.json"),candidate);
}
Console.WriteLine($"V3 PIT dataset checks passed: {count}; synthetic verification, not real research validation.");
