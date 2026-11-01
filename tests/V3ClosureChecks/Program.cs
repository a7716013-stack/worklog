using System.Net;
using System.Text;
using WorkJournal.Web.Models.Evidence;
using WorkJournal.Web.Models.HistoricalResearch;
using WorkJournal.Web.Services.HistoricalResearch;
using WorkJournal.Web.Services.Research;
int count=0;
void Check(bool ok,string name){if(!ok)throw new Exception(name);count++;Console.WriteLine("PASS: "+name);}
void Reject(Action action,string name){try{action();}catch(Exception e) when(e is ArgumentException or InvalidOperationException or InvalidDataException or IOException or System.Text.Json.JsonException){Check(true,name);return;}throw new Exception("Expected rejection: "+name);}
var source=Fixture.Create().Submission;var data=source.Dataset;var now=data.CreatedAt;
var root=Path.Combine(Path.GetTempPath(),"workjournal-closure-"+Guid.NewGuid().ToString("N"));
var evidenceRoot=Path.Combine(root,"evidence");var closureRoot=Path.Combine(root,"closure");
var acquisitions=new EvidenceAcquisitionStore(evidenceRoot);var closures=new AuthorityClosureStore(closureRoot,evidenceRoot);
var start=data.Calendar.Sessions.Min();var end=data.Calendar.Sessions.Max();
var tasks=Enum.GetValues<ClosureDomain>().Select(domain=>new EvidenceAcquisitionTask(Guid.NewGuid(),"fixture",domain.ToString(),AuthorityClosureStore.EvidenceDomainFor(domain),start,end,
    AvailabilityEvidenceType.OfficialSpecification,AcquisitionStatus.Pending,0,null,"","https://www.twse.com.tw/fixture/"+domain,null,"")).ToArray();
using var http=new HttpClient(new Transport());
var manifest=await acquisitions.Acquire(new(Guid.NewGuid(),"fixture",start,end,tasks),http,new FixedClock(now.AddDays(-1)));
var links=new List<ComponentClosureBinding>();var opened=new List<AuthorityClosureCase>();
foreach(var domain in Enum.GetValues<ClosureDomain>())
{
    var a=manifest.CapturedArtifacts.Single(x=>x.DatasetType==domain.ToString());var claims=AuthorityClosureStore.RequiredClaims(domain,true);
    var c=new AuthorityClosureCase(Guid.NewGuid(),domain,"fixture",domain.ToString(),start,end,claims,[],[],ClosureStatus.OPEN,[],now,null,null,"",AuthorityClosureStore.Subject(data,domain));opened.Add(c);
    foreach(var state in Enum.GetValues<ClosureStatus>().Where(s=>s!=ClosureStatus.OPEN))Reject(()=>closures.Open(c with{CaseId=Guid.NewGuid(),CurrentStatus=state}),"case cannot begin "+state);
    var r=closures.Open(c);Reject(()=>closures.Open(c),"case history cannot overwrite");
    Reject(()=>closures.Review(new(c.CaseId,r.Digest,ClosureStatus.APPROVED,[],[],now,"reviewer","skip")),"cannot skip evidence and review");
    r=closures.Review(new(c.CaseId,r.Digest,ClosureStatus.EVIDENCE_CAPTURED,[a.EvidenceId],[],now,"reviewer","captured fixture"));
    Check(r.Case.CurrentStatus==ClosureStatus.EVIDENCE_CAPTURED&&r.Case.ClosedAt is null,"capture is not closure");
    r=closures.Review(new(c.CaseId,r.Digest,ClosureStatus.UNDER_REVIEW,[],[],now,"reviewer","review claims"));
    var partial=acquisitions.Review(new(Guid.NewGuid(),a.EvidenceId,"fixture reviewer",now,"TEST_ONLY first claim",[claims[0]],[],[],start,end,"UTC","Not real evidence",a.SHA256,""));
    Reject(()=>closures.Review(new(c.CaseId,"stale",ClosureStatus.PARTIALLY_SUPPORTED,[],[partial.ReviewDigest],now,"reviewer","stale")),"stale closure review rejected");
    Reject(()=>closures.Review(new(c.CaseId,r.Digest,ClosureStatus.APPROVED,[],[partial.ReviewDigest],now,"reviewer","too broad")),"one claim cannot approve entire domain");
    r=closures.Review(new(c.CaseId,r.Digest,ClosureStatus.PARTIALLY_SUPPORTED,[],[partial.ReviewDigest],now,"reviewer","limited evidence"));
    Check(r.Claims.Count(x=>x.Status=="SUPPORTED")==1&&r.Case.BlockingReasons.Length==claims.Length-1&&r.Case.ClosedAt is null,"partial status retains every missing claim");
    r=closures.Review(new(c.CaseId,r.Digest,ClosureStatus.UNDER_REVIEW,[],[],now,"reviewer","additional review"));
    var complete=acquisitions.Review(new(Guid.NewGuid(),a.EvidenceId,"fixture reviewer",now,"TEST_ONLY remaining claims",claims.Skip(1).ToArray(),[],[],start,end,"UTC","Not real evidence",a.SHA256,""));
    r=closures.Review(new(c.CaseId,r.Digest,ClosureStatus.APPROVED,[],[complete.ReviewDigest],now,"reviewer","fixture claims supported"));
    Check(r.Case.ClosedAt==now&&r.Claims.All(x=>x.Status=="SUPPORTED")&&r.Case.ReviewIds.Length==2,"multiple scoped reviews close required claims");
    Check(closures.Read(c.CaseId).Digest==r.Digest,"closure deterministic replay");
    Reject(()=>closures.Review(new(c.CaseId,r.Digest,ClosureStatus.UNDER_REVIEW,[],[],now,"reviewer","mutate")),"approved case cannot mutate");
    links.Add(new(domain,c.CaseId,r.Digest));
}
var dataset=new RealDatasetSubmission(source,[],[EvidenceCode.Trend,EvidenceCode.Momentum,EvidenceCode.VolumePrice],null);
dataset=dataset with{Review=new(RealDataApproval.InputDigest(dataset),AuthorityStatus.Approved,now,"fixture reviewer","TEST_ONLY")};
var request=new ComponentClosureRequest(dataset,links.ToArray());var ready=closures.Evaluate(request);
var baseline=new RealBaselineStore(Path.Combine(root,"baseline"));
Reject(()=>baseline.Execute(dataset,null!,evidenceRoot,"unused",TimeProvider.System,ready),"new baseline cannot accept a receipt without live evidence store");
Check(ready.Status=="CLOSURE_READY_REQUIRES_DATASET_APPROVAL"&&ready.RequiredDomains.Length==6,"six closures enable only next dataset approval stage");
Check(!ready.RequiredDomains.Select(x=>x.ToString()).Any(x=>x.Contains("Benchmark")||x.Contains("Institutional")),"first component closure graph excludes benchmark and institutional");
Check(new RealDataApproval(evidenceRoot,Path.Combine(root,"empty-authority")).Evaluate(dataset).ComponentApprovals.Length==0,"closure cannot bypass PIT and authority approval");
foreach(var domain in Enum.GetValues<ClosureDomain>())Check(closures.Evaluate(request with{Closures=links.Where(x=>x.Domain!=domain).ToArray()}).Status=="BLOCKED_BY_AUTHORITY_OR_DATA","missing closure blocks "+domain);
Check(closures.Evaluate(request with{Closures=links.Append(links[0]).ToArray()}).Status=="BLOCKED_BY_AUTHORITY_OR_DATA","duplicate binding rejected");
Reject(()=>closures.Evaluate(request with{Dataset=dataset with{RequestedComponents=[EvidenceCode.RelativeStrength]}}),"first component scope cannot silently expand");
Check(closures.Evaluate(request with{Dataset=dataset with{Source=source with{Dataset=data with{Calendar=data.Calendar with{Version="changed"}}}}}).Status=="BLOCKED_BY_AUTHORITY_OR_DATA","same claims cannot approve changed policy");
var calendar=opened[0] with{CaseId=Guid.NewGuid()};var artifact=manifest.CapturedArtifacts[0];
var r2=closures.Open(calendar);r2=closures.Review(new(calendar.CaseId,r2.Digest,ClosureStatus.EVIDENCE_CAPTURED,[artifact.EvidenceId],[],now,"reviewer","capture"));
r2=closures.Review(new(calendar.CaseId,r2.Digest,ClosureStatus.UNDER_REVIEW,[],[],now,"reviewer","review"));
var negative=acquisitions.Review(new(Guid.NewGuid(),artifact.EvidenceId,"fixture reviewer",now,"contradiction",[],[calendar.RequiredClaims[0]],[],start,end,"UTC","TEST_ONLY",artifact.SHA256,""));
var positive=acquisitions.Review(new(Guid.NewGuid(),artifact.EvidenceId,"fixture reviewer",now,"all claims",calendar.RequiredClaims,[],[],start,end,"UTC","TEST_ONLY",artifact.SHA256,""));
Reject(()=>closures.Review(new(calendar.CaseId,r2.Digest,ClosureStatus.APPROVED,[],[negative.ReviewDigest,positive.ReviewDigest],now,"reviewer","ignore conflict")),"conflicting reviews block approval");
var narrow=acquisitions.Review(new(Guid.NewGuid(),artifact.EvidenceId,"fixture reviewer",now,"narrow",calendar.RequiredClaims,[],[],start.AddDays(1),end,"UTC","TEST_ONLY",artifact.SHA256,""));
Reject(()=>closures.Review(new(calendar.CaseId,r2.Digest,ClosureStatus.APPROVED,[],[narrow.ReviewDigest],now,"reviewer","broaden")),"narrow review cannot approve broader history");
Reject(()=>closures.Review(new(calendar.CaseId,r2.Digest,ClosureStatus.APPROVED,[],[positive.ReviewDigest],now.AddDays(-1),"reviewer","backdate")),"backdated review rejected");
var wrong=manifest.CapturedArtifacts.Single(x=>x.DatasetType==ClosureDomain.Availability.ToString());
Reject(()=>closures.Review(new(calendar.CaseId,r2.Digest,ClosureStatus.BLOCKED,[wrong.EvidenceId],[],now,"reviewer","wrong domain")),"evidence cannot cross dataset identity");
r2=closures.Review(new(calendar.CaseId,r2.Digest,ClosureStatus.REJECTED,[],[negative.ReviewDigest],now,"reviewer","reject"));
Check(r2.Case.ClosedAt==now,"rejected case closes explicitly");
Reject(()=>closures.Review(new(calendar.CaseId,r2.Digest,ClosureStatus.APPROVED,[],[positive.ReviewDigest],now,"reviewer","reopen")),"rejected case requires new identity");
var prior=closures.Read(links[0].CaseId);var superseded=closures.Review(new(prior.Case.CaseId,prior.Digest,ClosureStatus.SUPERSEDED,[],[],now,"reviewer","replacement"));
Check(superseded.Case.CurrentStatus==ClosureStatus.SUPERSEDED&&closures.Evaluate(request).Status=="BLOCKED_BY_AUTHORITY_OR_DATA","superseded closure blocks new research");
Reject(()=>baseline.Execute(dataset,null!,evidenceRoot,"unused",TimeProvider.System,ready,closureRoot),"baseline rechecks live closure revisions before calculating outcomes");
var file=Path.Combine(closureRoot,"cases",links[1].CaseId.ToString("N"),"00000000.json");File.AppendAllText(file,"x");
Reject(()=>closures.Read(links[1].CaseId),"corrupted archive rejected");
Console.WriteLine($"V3 closure checks passed: {count}; fixture evidence only, no real component approval.");
sealed class FixedClock(DateTimeOffset value):TimeProvider{public override DateTimeOffset GetUtcNow()=>value;}
sealed class Transport:HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r,CancellationToken token)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {RequestMessage=r,Content=new StringContent("SIMULATED closure evidence only",Encoding.UTF8,"text/plain")});
}
