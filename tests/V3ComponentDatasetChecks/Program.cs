using System.Collections.Immutable;
using System.Net;
using System.Text;
using System.Text.Json;
using WorkJournal.Web.Models.Evidence;
using WorkJournal.Web.Models.HistoricalResearch;
using WorkJournal.Web.Models.HistoricalResearch.Components;
using WorkJournal.Web.Services.HistoricalResearch;
using WorkJournal.Web.Services.Research;
int checks=0;
void Check(bool condition,string name){if(!condition)throw new Exception(name);checks++;Console.WriteLine("PASS: "+name);}
void Reject(Action action,string name){try{action();}catch(Exception e) when(e is ArgumentException or InvalidOperationException or InvalidDataException or IOException or System.Text.Json.JsonException){Check(true,name);return;}throw new Exception("Expected rejection: "+name);}
var date=new DateOnly(2024,1,1);var at=new DateTimeOffset(2024,1,1,18,0,0,TimeSpan.Zero);var evidence=Guid.NewGuid();var instrument=Guid.NewGuid();
var session=new HistoricalTradingSession("TWSE",date,SessionKind.NormalTradingDay,true,new(9,0),new(13,30),at.AddHours(-18),at,evidence,"calendar-v1");
foreach(var kind in Enum.GetValues<SessionKind>())
{
    var row=session with{SessionType=kind,IsTradingDay=kind is SessionKind.NormalTradingDay or SessionKind.SpecialSession};
    var c=ComponentEvidence.Calendar([row],date,date,at);
    Check(kind==SessionKind.Unknown?c.ExpectedSessions is null:c.UnknownSessions==0,"calendar distinguishes "+kind);
}
Check(ComponentEvidence.Calendar([],date,date.AddDays(1),at).Coverage is null,"missing denominator remains null");
Check(ComponentEvidence.Calendar([session with{AvailableAt=at.AddSeconds(1)}],date,date,at).UnknownSessions==1,"future-known calendar excluded");
Check(ComponentEvidence.Calendar([session,session],date,date,at).ExpectedSessions is null,"conflicting calendar not silently selected");
Check(ComponentEvidence.Calendar([session with{CloseTime=new(8,0)}],date,date,at).ExpectedSessions is null,"invalid session hours rejected");
var identity=new HistoricalInstrumentIdentityRecord(instrument,"TWSE","2330",at.AddYears(-1),at,date.AddYears(-1),date,null,"NEW",null,null,at.AddYears(-1),at.AddYears(-1),evidence,"original");
var next=identity with{InstrumentId=Guid.NewGuid(),ValidFrom=at,ValidTo=null,EffectiveAt=at};
Check(ComponentEvidence.ResolveInstrument([identity,next],"TWSE","2330",at.AddSeconds(-1),at)==instrument,"historical identity retained despite later reuse");
Check(ComponentEvidence.ResolveInstrument([identity,next],"TWSE","2330",at,at)==next.InstrumentId,"half-open identity boundary");
Check(ComponentEvidence.ResolveInstrument([identity],"TWSE","2330",at,at) is null,"expired mapping cannot backfill");
Check(ComponentEvidence.ResolveInstrument([next with{Symbol="NEW"}],"TWSE","2330",at,at) is null,"symbol change is temporal");
Check(ComponentEvidence.ResolveInstrument([next with{Market="TPEX",MarketTransferFrom="TWSE"}],"TWSE","2330",at,at) is null,"market transfer cannot use latest mapping");
Check(ComponentEvidence.ResolveInstrument([next with{AvailableAt=null}],"TWSE","2330",at,at) is null,"unknown historical mapping blocked");
Check(ComponentEvidence.ResolveInstrument([next,next],"TWSE","2330",at,at) is null,"ambiguous mapping rejected");
var fixture=Fixture.Create();var f=fixture.Submission.Dataset.Facts[0];
var claim=new HistoricalAvailabilityClaim(f.Provider,f.DatasetType,f.TradeDate,"verified","UTC",f.AvailableAt,null,[evidence],AuthorityLevel.OfficialTimestamp,AvailabilityConfidence.Known,"v1",f.FactChecksum,new string('A',64));
foreach(var level in Enum.GetValues<AuthorityLevel>())Check(ComponentEvidence.Availability(f,claim with{EvidenceType=level},at.AddYears(2))==(level<=AuthorityLevel.OfficialDocumentation?ComponentAvailability.Available:ComponentAvailability.Unverifiable),"authority strength "+level);
Check(ComponentEvidence.Availability(f,claim with{Confidence=AvailabilityConfidence.DerivedFromVerifiedRule},at.AddYears(2))==ComponentAvailability.Available,"derived claim eligible for rule verification gate");
Check(ComponentEvidence.Availability(f,claim with{Confidence=AvailabilityConfidence.Unknown},at.AddYears(2))==ComponentAvailability.Unverifiable,"unknown never becomes known");
Check(ComponentEvidence.Availability(f,claim with{OriginalAvailableAt=null},at.AddYears(2))==ComponentAvailability.Unverifiable,"retrieved/trade date cannot substitute publication");
Check(ComponentEvidence.Availability(f,claim with{OriginalAvailableAt=at.AddYears(3)},at)==ComponentAvailability.FutureKnown,"future publication excluded");
Check(ComponentEvidence.Availability(f with{Value=f.Value with{Close=null}},claim,at.AddYears(2))==ComponentAvailability.Missing,"missing close stays missing");
Check(ComponentEvidence.PriceBasis("RAW","RAW","v1",true),"raw/raw passes");
Check(ComponentEvidence.PriceBasis("ADJUSTED","ADJUSTED","v1",true),"adjusted/adjusted passes");
Check(!ComponentEvidence.PriceBasis("RAW","ADJUSTED","v1",true),"mixed basis rejected");
Check(!ComponentEvidence.PriceBasis("UNKNOWN","UNKNOWN","v1",true),"unknown basis rejected");
Check(!ComponentEvidence.PriceBasis("ADJUSTED","ADJUSTED","",true),"unknown adjustment policy blocks");
var action=new HistoricalCorporateAction(instrument,"CashDividend",date,null,date,null,at,evidence,null);
var actions=ComponentEvidence.VisibleActions([action,action with{AvailableAt=at.AddDays(1)},action with{AvailableAt=null}],at);
Check(actions.Length==1&&actions[0].AdjustmentFactor is null,"known action preserved; future excluded; adjustment never invented");
foreach(var code in new[]{EvidenceCode.Trend,EvidenceCode.Momentum,EvidenceCode.VolumePrice})
{
    var manifest=ComponentEvidence.Dependencies(code,Guid.NewGuid(),at);
    Check(!manifest.RequiredDatasets.Contains("Institutional")&&manifest.ExcludedDependencies.Contains("BenchmarkReturns")&&manifest.RequiredIdentities.Contains("BenchmarkVersionIdentity"),"identity/data dependencies separated for "+code);
    Check(manifest.RequiredDatasets.Contains("HistoricalVolume")== (code==EvidenceCode.VolumePrice),"volume dependency only VolumePrice");
}
var root=Path.Combine(Path.GetTempPath(),"component-checks-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
var store=new ComponentDatasetStore(root,root,root,root);var d=fixture.Submission.Dataset;
var req=new ComponentDatasetApprovalRequest(EvidenceCode.Trend,"V3EvidenceCandidate-v1",d.ParameterSetId,d.Dates.Min(x=>x.TradeDate),d.Dates.Max(x=>x.TradeDate),"fixture-universe-v1",d.PricePolicy.PriceBasisVersion,[],d.Policies.Where(p=>p.DatasetType==HistoricalDatasetType.Price).Select(p=>p.PolicyId).ToArray(),"fixture",d.CreatedAt,"");
req=req with{RequestDigest=ResearchMath.Digest(req)};
var input=new ComponentDatasetInput(ComponentDataMode.Synthetic,req,new(new(fixture.Submission,[],[EvidenceCode.Trend],null),[]),[],[],[],[],[],[],[],[]);
var result=store.Evaluate(input);
Check(!ComponentEvidence.ProviderRevision(f.SourceChecksum,f.SourceChecksum)&&!ComponentEvidence.ProviderRevision("captured-body:"+f.SourceChecksum,f.SourceChecksum),"checksum and captured-body labels are not provider revision proof");
Check(result.KnownGaps.Contains("REVISION_UNKNOWN"),"unknown revision policy blocks approval");
Check(result.KnownGaps.Contains("CALENDAR_COVERAGE_UNKNOWN")&&result.KnownGaps.Contains("SURVIVORSHIP_BIAS_RISK"),"missing calendar and historical membership are explicit blockers");
Check(result.KnownGaps.Contains("REAL_SOURCE_REQUIRED")&&result.Status!="READY_FOR_EXPLICIT_APPROVAL","synthetic fixture cannot approve real dataset");
Reject(()=>store.Approve(input),"no required authority cannot issue receipt");
Check(Directory.GetFiles(Path.Combine(root,"failures")).Length==1,"failed approval leaves durable failure artifact");
Reject(()=>store.Baseline(Guid.NewGuid(),null!,new string('a',40)),"no approval receipt rejects baseline");
var again=store.Evaluate(input);Check(again.Digest==result.Digest,"frozen input evaluation deterministic");
Check(store.Evaluate(input with{Request=req with{PriceBasisVersion="changed"}}).Digest!=result.Digest,"policy change changes result digest");
Check(result.Coverage.ExpectedTradingDates is null&&result.Coverage.ExpectedUniverseMembers is null,"unknown coverage denominators remain null");
// Network-free official-shape payload, explicitly synthetic transport. Never used to issue an approval.
var capture=new EvidenceAcquisitionStore(Path.Combine(root,"raw"));
var task=new EvidenceAcquisitionTask(Guid.NewGuid(),"TWSE","HistoricalPriceVolume",EvidenceDomain.Availability,date,date.AddDays(30),AvailabilityEvidenceType.OfficialArchiveMetadata,AcquisitionStatus.Pending,0,null,"","https://www.twse.com.tw/rwd/zh/afterTrading/STOCK_DAY?date=20240101&stockNo=2330&response=json",null,"");
using var client=new HttpClient(new Transport());
var captured=await capture.Acquire(new(Guid.NewGuid(),"TWSE",date,date.AddDays(30),[task]),client,TimeProvider.System);
var id=captured.CapturedArtifacts.Single().EvidenceId;var rows=ComponentEvidence.ParsePrice(Path.Combine(root,"raw"),id);
Check(rows.Length==2&&rows[0].Locator.RecordIndex==0&&rows[0].Locator.Symbol=="2330"&&rows[0].Locator.TradeDate==date,"raw locator binds response row symbol date");
Check(rows[1].Value.Volume is null&&rows[1].Value.Close is null,"parser does not convert unavailable prices/volume to zero");
Check(ResearchMath.Digest(rows)==ResearchMath.Digest(ComponentEvidence.ParsePrice(Path.Combine(root,"raw"),id)),"raw parse deterministic replay");
File.AppendAllText(Path.Combine(root,"raw","evidence","authority",id.ToString("N"),"raw.bin"),"x");
Reject(()=>ComponentEvidence.ParsePrice(Path.Combine(root,"raw"),id),"raw digest corruption rejected");
Console.WriteLine($"V3 component dataset checks passed: {checks}; simulated fixtures, no real approval or research milestone.");
sealed class Transport:HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){RequestMessage=request,Content=new StringContent("{\"stat\":\"OK\",\"fields\":[\"日期\",\"成交股數\",\"成交金額\",\"開盤價\",\"最高價\",\"最低價\",\"收盤價\"],\"data\":[[\"113/01/01\",\"1,000\",\"5000\",\"10\",\"12\",\"9\",\"11\"],[\"113/01/02\",\"--\",\"--\",\"--\",\"--\",\"--\",\"--\"]]}",Encoding.UTF8,"application/json")});
}
