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
int checks=0;
void Check(bool value,string name){if(!value)throw new Exception(name);checks++;Console.WriteLine("PASS: "+name);}
void Reject(Action action,string name){try{action();}catch(Exception e) when(e is InvalidDataException or InvalidOperationException or IOException or ArgumentException){Check(true,name);return;}throw new Exception("Expected rejection: "+name);}
Guid Id(int n)=>Guid.Parse($"00000000-0000-0000-0000-{n:000000000000}");
var dates=Enumerable.Range(0,150).Select(i=>new DateOnly(2024,1,1).AddDays(i)).ToImmutableArray();
DateTimeOffset At(DateOnly d)=>new(d.ToDateTime(new TimeOnly(18,0)),TimeSpan.Zero);
var created=At(dates[^1]).AddDays(1);var before=At(dates[0]).AddDays(-1);
var ids=new[]{Id(1),Id(2),Id(3)};var benchmark=Id(4);
var p=new V3EvidenceCandidateV1ParameterSet{SlopeHorizon=5,StructureHorizon=10,AlignmentBounds=new(-.1m,.1m),PositionBounds=new(-.1m,.1m),SlopeBounds=new(-.1m,.1m),
    RocHorizon=5,MacdBounds=new(-.01m,.01m),RocBounds=new(-.1m,.1m),VolumeHorizon=5,ReturnHorizon=5,BreakoutHorizon=5,RvolLogBounds=new(-1,1),PvcBounds=new(-.1m,.1m),BreakoutBounds=new(-.1m,.1m),
    InstitutionalHorizons=[2,3,5],PersistenceHorizon=5,Persistence=PersistenceGroup.Combined,RelativeStrengthHorizons=[2,3,5],VolatilityHorizon=5,Variance=VarianceDefinition.Population,DrawdownHorizon=5,AtrPeriod=5,Atr=AtrDefinition.SimpleMean,LiquidityHorizon=5,LiquidityPrice="Close"};
var archives=Enum.GetValues<HistoricalDatasetType>().Select(t=>
{
    var bytes=Encoding.UTF8.GetBytes("SIMULATED AUTHORITY FIXTURE ONLY "+t);
    // False deliberately exercises the real-data gate with fabricated test metadata; never external research evidence.
    return new SourceArchive(t.ToString(),"fixture",t,"fixture://authority/"+t,created,created,Convert.ToBase64String(bytes),Convert.ToHexString(SHA256.HashData(bytes)),"fixture-v1",false);
}).ToImmutableArray();
var policies=archives.Select((a,i)=>new FrozenAvailabilityPolicy(Id(10+i),"fixture",a.DatasetType,"fixture-publication-v1",dates[0],null,AvailabilityRule.SourceTimestamp,"UTC",null,"fixture://spec","Simulated original revisions","fixture reviewer",before,before,FoundationStatus.Finalized)).ToImmutableArray();
var facts=new List<HistoricalFact>();
foreach(var a in archives)foreach(var id in a.DatasetType==HistoricalDatasetType.Benchmark?new[]{benchmark}:ids)
foreach(var (date,i) in dates.Select((d,i)=>(d,i)))
{
    var n=Array.IndexOf(ids,id)+1;var price=100+i*(n+1)*.1m+(i%3)*.1m;
    var value=a.DatasetType==HistoricalDatasetType.Institutional?new HistoricalValue(Foreign:10+n,Trust:5,Dealer:1):new(price,price+1,price-1,price,1000+i,100000+i);
    var f=new HistoricalFact("fixture",a.DatasetType,id.ToString(),date,At(date).AddHours(-18),At(date),created,created,"fixture-revision",$"{a.DatasetType}/{id}/{date:yyyy-MM-dd}",a.ArchiveId,value,"Shares","TWD",EvidenceStatus.Available,a.SourceChecksum,"",policies.Single(x=>x.DatasetType==a.DatasetType).PolicyId,AvailabilityConfidence.Known,"fixture publication","fixture revision","fixture-raw-v1");
    facts.Add(f with{FactChecksum=HistoricalAvailability.FactChecksum(f)});
}
var days=new[]{dates[130],dates[135],dates[140]};
var universes=days.Select((d,i)=>
{
    var u=new HistoricalUniverse(Id(30+i),"fixture-universe-v1",d,before,ids.ToImmutableArray(),"Simulated membership","fixture://universe",true,"");
    return u with{Checksum=HistoricalAvailability.UniverseChecksum(u)};
}).ToImmutableArray();
var dataset=new HistoricalBuildRequest("authority-test-v1","pit-builder-v1",created,Id(20),Id(21),V3CandidateCalculator.ParameterChecksum(p),new("fixture-calendar-v1",dates,before,"fixture://calendar"),
    new("fixture-raw-v1","Raw","fixture-corporate-actions-v1","fixture://actions","fixture reviewer",true),days.Select(d=>new HistoricalResearchDate(d,At(d).AddHours(1))).ToImmutableArray(),archives,facts.ToImmutableArray(),policies,
    ids.Append(benchmark).Select(id=>new HistoricalInstrumentMapping("fixture",id.ToString(),id,dates[0],null,before,"fixture://identity","fixture-id-v1")).ToImmutableArray(),universes,
    ids.Select(id=>new HistoricalBenchmarkMapping(id,benchmark,Id(22),dates[0],null,before,"fixture-benchmark-v1","fixture://benchmark")).ToImmutableArray());
var evidence=archives.Select((a,i)=>
{
    var bytes=Encoding.UTF8.GetBytes("SIMULATED PUBLICATION AND REVISION SPECIFICATION "+a.DatasetType);
    return new HistoricalAvailabilityEvidence(Id(40+i),"fixture",a.DatasetType,a.SourceUri,AvailabilityEvidenceType.OfficialSpecification,"fixture://spec",before,
        Convert.ToHexString(SHA256.HashData(bytes)),"fixture-publication-v1",dates[0],null,"UTC",EvidenceVerificationStatus.Verified,"Test reviewer attestation only",Convert.ToBase64String(bytes));
}).ToImmutableArray();
var authorities=archives.Select((a,i)=>new HistoricalDataAuthority(Id(50+i),"fixture",a.DatasetType,a.SourceUri,"fixture-publication-v1","fixture-revision-v1","fixture-calendar-v1","fixture-raw-v1",dates[0],null,[Id(40+i)],created,"fixture reviewer",AuthorityStatus.Approved,"TEST_ONLY",HistoricalAuthority.PolicyScopeDigest(dataset,policies[i]))).ToImmutableArray();
var bindings=dataset.Facts.Select(f=>new AuthorityFactBinding(f.FactChecksum,authorities.Single(a=>a.DatasetType==f.DatasetType).AuthorityId,"fixture-revision-v1")).ToImmutableArray();
var submission=new AuthoritySubmission(dataset,authorities,evidence,bindings,created,null);
var digest=HistoricalAuthority.SubmissionDigest(submission);
submission=submission with{Review=new(digest,AuthorityStatus.Approved,created,"fixture reviewer","TEST_ONLY complete dataset review")};
var f0=dataset.Facts[0];var a0=authorities[0];
Check(HistoricalAuthority.Authorize(submission,f0).Authorized,"approved scoped authority authorizes verified fact");
foreach(var status in Enum.GetValues<AuthorityStatus>().Where(x=>x!=AuthorityStatus.Approved))
    Check(!HistoricalAuthority.Authorize(submission with{Authorities=authorities.SetItem(0,a0 with{Status=status})},f0).Authorized,"reject authority state "+status);
Check(!HistoricalAuthority.Authorize(submission with{Authorities=[]},f0).Authorized,"official provider name is not approval");
Check(!HistoricalAuthority.Authorize(submission with{Bindings=[]},f0).Authorized,"explicit fact authority binding required");
Check(!HistoricalAuthority.Authorize(submission with{Bindings=bindings.Add(bindings[0])},f0).Authorized,"ambiguous fact binding rejected");
Check(!HistoricalAuthority.Authorize(submission with{Dataset=dataset with{Policies=policies.SetItem(0,policies[0] with{LocalPublicationTime=new(23,0)})}},f0).Authorized,"same version cannot silently change availability policy");
Check(!HistoricalAuthority.Authorize(submission with{Dataset=dataset with{Calendar=dataset.Calendar with{Sessions=dates.RemoveAt(1)}}},f0).Authorized,"same calendar version cannot silently change sessions");
Check(!HistoricalAuthority.Authorize(submission with{Dataset=dataset with{PricePolicy=dataset.PricePolicy with{Basis="Adjusted"}}},f0).Authorized,"same price version cannot silently change basis policy");
foreach(var changed in new[]{a0 with{Provider="TWSE"},a0 with{SourceIdentity="fixture://other"},a0 with{DatasetType=HistoricalDatasetType.Benchmark},a0 with{AvailabilityPolicyVersion="other"},a0 with{RevisionPolicyVersion="other"},a0 with{CalendarPolicyVersion="other"},a0 with{PriceBasisVersion="other"},a0 with{EffectiveFrom=dates[1]},a0 with{EffectiveTo=dates[0].AddDays(-1)},a0 with{ReviewedAt=created.AddDays(1)},a0 with{ReviewedBy=""}})
    Check(!HistoricalAuthority.Authorize(submission with{Authorities=authorities.SetItem(0,changed)},f0).Authorized,"authority identity/policy/date/review mismatch blocked");
foreach(var changed in new[]{evidence[0] with{VerificationStatus=EvidenceVerificationStatus.Unverified},evidence[0] with{VerificationStatus=EvidenceVerificationStatus.Rejected},evidence[0] with{EvidenceDigest="bad"},evidence[0] with{EvidenceCapturedAt=created.AddDays(1)},evidence[0] with{SourceIdentity="other"},evidence[0] with{Timezone="Asia/Taipei"},evidence[0] with{ClaimedAvailabilityRule="other"},evidence[0] with{EffectiveFrom=dates[1]}})
    Check(!HistoricalAuthority.Authorize(submission with{Evidence=evidence.SetItem(0,changed)},f0).Authorized,"unverified/tampered/out-of-scope evidence blocked");
var unknown=f0 with{Confidence=AvailabilityConfidence.Unknown,AvailableAt=null};unknown=unknown with{FactChecksum=HistoricalAvailability.FactChecksum(unknown)};
Check(!HistoricalAuthority.Authorize(submission with{Bindings=bindings.SetItem(0,bindings[0] with{FactChecksum=unknown.FactChecksum})},unknown).Authorized,"Approved authority never supplies unknown AvailableAt");
var approved=HistoricalAuthority.Evaluate(submission);
Check(approved.Status=="APPROVED_PIT_DATASET","explicit dataset approval permits complete fixture only");
Check(HistoricalAuthority.Evaluate(submission with{Review=null}).Status!="APPROVED_PIT_DATASET","no implicit dataset approval");
Check(HistoricalAuthority.Evaluate(submission with{Review=submission.Review! with{SubmissionDigest="other"}}).Status!="APPROVED_PIT_DATASET","review receipt binds exact submission");
Check(HistoricalAuthority.Evaluate(submission with{Dataset=dataset with{Archives=archives.Select(a=>a with{Synthetic=true}).ToImmutableArray()}}).Status!="APPROVED_PIT_DATASET","synthetic fixture cannot be promoted to real approval");
Check(HistoricalAuthority.SubmissionDigest(submission with{Authorities=authorities.Reverse().ToImmutableArray(),Evidence=evidence.Reverse().ToImmutableArray(),Bindings=bindings.Reverse().ToImmutableArray()})==digest,"authority set order deterministic");
var root=Path.Combine(Path.GetTempPath(),"workjournal-authority-"+Guid.NewGuid().ToString("N"));var store=new AuthorityStore(root);
foreach(var e in evidence)store.Evidence(e);
Reject(()=>store.Evidence(evidence[0]),"evidence archive immutable");
foreach(var a in authorities)
{
    var draft=a with{Status=AuthorityStatus.Draft,ReviewedAt=null,ReviewedBy="",ReasonCode="TEST_ONLY draft"};
    var r=store.Register(draft);
    Reject(()=>store.Review(a.AuthorityId,r.Digest,AuthorityStatus.Approved,created,"reviewer","skip"),"cannot skip evidence collection and review");
    foreach(var status in new[]{AuthorityStatus.EvidenceCollected,AuthorityStatus.UnderReview,AuthorityStatus.Approved})
        r=store.Review(a.AuthorityId,r.Digest,status,created,a.ReviewedBy,a.ReasonCode);
    Check(ResearchMath.Digest(store.Read(a.AuthorityId).Authority)==ResearchMath.Digest(a),"durable ordered review history");
}
Reject(()=>store.Register(a0),"cannot register preapproved authority");
Reject(()=>store.Review(a0.AuthorityId,"stale",AuthorityStatus.Superseded,created,"reviewer","reason"),"optimistic review digest required");
var archived=store.EvaluateAndSave(submission);
Check(store.Replay(archived.ReportDigest).Report.ReportDigest==archived.ReportDigest,"approved archive deterministic replay");
Reject(()=>store.EvaluateAndSave(submission),"approved dataset audit cannot overwrite");
Reject(()=>store.Replay("../escape"),"archive path traversal rejected");
var outcomes=new OutcomeDataset("authority-test-labels","fixture-revision-v1",created,dates,ids.Select(id=>new OutcomeSeries(id,"fixture-raw-v1",facts.Where(f=>f.SourceInstrumentKey==id.ToString()&&f.DatasetType==HistoricalDatasetType.Price).Select(f=>new OutcomeClose(f.TradeDate,f.Value.Close!.Value,f.AvailableAt,EvidenceStatus.Available)).ToImmutableArray())).ToImmutableArray());
var protocol=new ResearchProtocol("authority-fixture-protocol-v1",[new("F1",new(dates[130],dates[132]),new(dates[135],dates[137]),new(dates[140],dates[142]))],[1],3,2);
var template=new ResearchRequest(Id(99),created,V3CandidateCalculator.Version,"fixture-model-v1","CandidateFeatures-v1",p,protocol,[],outcomes,[],[],false,[]);
var researchRoot=Path.Combine(root,"research");
Reject(()=>new ResearchStore(researchRoot).Execute(template with{Snapshots=approved.Dataset.Snapshots}),"legacy research entry cannot bypass real data authority");
var resultPath=store.Research(archived.ReportDigest,template,researchRoot);
Check(File.Exists(Path.Combine(resultPath,"authority-approval.json")),"research persists exact authority evidence and dataset review");
Check(new ResearchStore(researchRoot).Replay(template.ResearchRunId).Status==ResearchRunStatus.Completed,"approved fixture runs through existing Candidate and ResearchEngine");
Reject(()=>store.Research(archived.ReportDigest,template with{ResearchRunId=Id(98),Outcomes=outcomes with{Series=outcomes.Series.Select(s=>s with{Closes=s.Closes.Select(c=>c with{Close=c.Close+1}).ToImmutableArray()}).ToImmutableArray()}},researchRoot),"outcome labels require authorized source provenance");
Reject(()=>store.Research(archived.ReportDigest,template with{ResearchRunId=Id(96),Outcomes=outcomes with{Sessions=outcomes.Sessions.RemoveAt(1)}},researchRoot),"outcome horizon cannot use an unreviewed calendar");
var current=store.Read(a0.AuthorityId);store.Review(a0.AuthorityId,current.Digest,AuthorityStatus.Superseded,created,"fixture reviewer","replacement required");
Check(store.Replay(archived.ReportDigest).Report.ReportDigest==archived.ReportDigest,"historical audit replay survives later supersession");
Reject(()=>store.Research(archived.ReportDigest,template with{ResearchRunId=Id(97)},researchRoot),"superseded authority blocks new research");
Reject(()=>store.EvaluateAndSave(submission),"stale approved authority cannot bypass current registry");
var evidenceFile=Path.Combine(root,"evidence",evidence[0].EvidenceId.ToString("N")+".json");
File.WriteAllText(evidenceFile,File.ReadAllText(evidenceFile).Replace("Test reviewer attestation only","changed metadata"));
Reject(()=>store.ReadEvidence(evidence[0].EvidenceId),"evidence metadata tampering detected");
Console.WriteLine($"V3 Authority checks passed: {checks}; simulated authority metadata only, no real source approval.");
