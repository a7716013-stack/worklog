using System.Collections.Immutable;
using System.Net;
using System.Text;
using WorkJournal.Web.Models.Evidence;
using WorkJournal.Web.Models.HistoricalResearch;
using WorkJournal.Web.Models.Research;
using WorkJournal.Web.Models.V2;
using WorkJournal.Web.Services.Evidence;
using WorkJournal.Web.Services.HistoricalResearch;
using WorkJournal.Web.Services.Research;

int count=0;
void Check(bool ok,string label){if(!ok)throw new Exception(label);count++;Console.WriteLine("PASS: "+label);}
void Reject(Action action,string label){try{action();}catch(Exception e) when(e is ArgumentException or IOException or InvalidDataException or InvalidOperationException){Check(true,label);return;}throw new Exception("Expected rejection: "+label);}
var fixture=Fixture.Create();var source=fixture.Submission;var d=source.Dataset;var dates=d.Calendar.Sessions;
var at=d.CreatedAt;var clock=new FixedClock(at.AddDays(-1));
var root=Path.Combine(Path.GetTempPath(),"workjournal-real-contract-"+Guid.NewGuid().ToString("N"));
var evidenceRoot=Path.Combine(root,"acquisition");var authorityRoot=Path.Combine(root,"authority");var baselineRoot=Path.Combine(root,"baseline");
var store=new EvidenceAcquisitionStore(evidenceRoot);var authorities=new AuthorityStore(authorityRoot);
var plan=new AcquisitionPlan(Guid.NewGuid(),"fixture",dates[0],dates[^1],Enum.GetValues<EvidenceDomain>().Select(domain=>
    new EvidenceAcquisitionTask(Guid.NewGuid(),"fixture",domain.ToString(),domain,dates[0],dates[^1],AvailabilityEvidenceType.OfficialSpecification,
        AcquisitionStatus.Pending,0,null,"","https://www.twse.com.tw/fixture/"+domain,null,"")).ToArray());
using var client=new HttpClient(new FixtureTransport());
var acquired=await store.Acquire(plan,client,clock);
Check(acquired.CapturedArtifacts.Length==5&&acquired.Tasks.All(t=>t.Status==AcquisitionStatus.Captured),"capture never auto-verifies official sources");
Check(store.Replay(plan.AcquisitionRunId).OutputDigest==acquired.OutputDigest,"raw archive and manifest replay");
Reject(()=>store.Acquire(plan,client,clock).GetAwaiter().GetResult(),"same acquisition cannot overwrite earlier run");
Reject(()=>store.Acquire(plan with{AcquisitionRunId=Guid.NewGuid(),Tasks=[plan.Tasks[0] with{SourceReference="https://localhost/private"}]},client,clock).GetAwaiter().GetResult(),"non-provider endpoint rejected before request");
var failure=await store.Acquire(plan with{AcquisitionRunId=Guid.NewGuid(),Tasks=[plan.Tasks[0] with{SourceReference="https://www.twse.com.tw/failure"}]},client,clock);
Check(failure.FailedArtifacts.Length==1&&failure.CapturedArtifacts.Length==0,"HTTP failure retained in acquisition manifest");
var empty=await store.Acquire(plan with{AcquisitionRunId=Guid.NewGuid(),Tasks=[plan.Tasks[0] with{SourceReference="https://www.twse.com.tw/empty"}]},client,clock);
Check(empty.FailedArtifacts.Single().Reason=="EMPTY_BODY","empty response not evidence");
foreach(var e in source.Evidence)authorities.Evidence(e);
void Register(HistoricalDataAuthority a)
{
    var revision=authorities.Register(a with{Status=AuthorityStatus.Draft,ReviewedAt=null,ReviewedBy="",ReasonCode="TEST_ONLY"});
    foreach(var next in new[]{AuthorityStatus.EvidenceCollected,AuthorityStatus.UnderReview,AuthorityStatus.Approved})
        revision=authorities.Review(a.AuthorityId,revision.Digest,next,at,a.ReviewedBy,a.ReasonCode);
}
foreach(var a in source.Authorities)Register(a);
var domainBindings=new List<DomainAuthorityBinding>();var reviews=new List<EvidenceReviewReceipt>();
foreach(var artifact in acquired.CapturedArtifacts)
{
    var claims=RealDataApproval.RequiredClaims[artifact.Domain].Concat(new[]{"Availability:test:TimeWindow"}).ToArray();
    var review=store.Review(new(Guid.NewGuid(),artifact.EvidenceId,"fixture reviewer",at,"SIMULATED proof for contract tests only",claims,[],[],dates[0],dates[^1],"UTC","NOT real approval",artifact.SHA256,""));
    reviews.Add(review);
    var body=Encoding.UTF8.GetBytes(FixtureTransport.Body);
    var e=new HistoricalAvailabilityEvidence(artifact.EvidenceId,"fixture",HistoricalDatasetType.Price,artifact.SourceReference,
        AvailabilityEvidenceType.OfficialSpecification,artifact.SourceReference,artifact.CapturedAt,artifact.SHA256,"fixture-publication-v1",dates[0],dates[^1],"UTC",
        EvidenceVerificationStatus.Verified,"TEST_ONLY",Convert.ToBase64String(body));
    authorities.Evidence(e);
    var a=new HistoricalDataAuthority(Guid.NewGuid(),"fixture",HistoricalDatasetType.Price,artifact.SourceReference,"fixture-publication-v1","fixture-revision-v1",
        d.Calendar.Version,d.PricePolicy.PriceBasisVersion,dates[0],dates[^1],[e.EvidenceId],at,"fixture reviewer",AuthorityStatus.Approved,"TEST_ONLY",HistoricalAuthority.PolicyScopeDigest(d,d.Policies[0]));
    Register(a);source=source with{Authorities=source.Authorities.Add(a),Evidence=source.Evidence.Add(e)};
    domainBindings.Add(new(artifact.Domain,RealDataApproval.Subject(d,artifact.Domain),e.EvidenceId,review.ReviewDigest,a.AuthorityId,authorities.Read(a.AuthorityId).Digest,RealDataApproval.RequiredClaims[artifact.Domain],dates[0],dates[^1]));
}
var raw=acquired.CapturedArtifacts[0];var r0=reviews[0];
var taskState=store.TaskState(plan.AcquisitionRunId,raw.TaskId);
Reject(()=>store.Transition(plan.AcquisitionRunId,raw.TaskId,taskState.Digest,AcquisitionStatus.Verified,at,"reviewer","skip",r0.ReviewDigest),"cannot skip UnderReview acquisition state");
taskState=store.Transition(plan.AcquisitionRunId,raw.TaskId,taskState.Digest,AcquisitionStatus.UnderReview,at,"reviewer","inspect",null);
Reject(()=>store.Transition(plan.AcquisitionRunId,raw.TaskId,"stale",AcquisitionStatus.Verified,at,"reviewer","approve",r0.ReviewDigest),"stale acquisition review rejected");
taskState=store.Transition(plan.AcquisitionRunId,raw.TaskId,taskState.Digest,AcquisitionStatus.Verified,at,"reviewer","fixture claims",r0.ReviewDigest);
Check(store.TaskState(plan.AcquisitionRunId,raw.TaskId).Task.Status==AcquisitionStatus.Verified,"acquisition verification binds durable review receipt");
Reject(()=>store.Review(r0 with{ReviewId=Guid.NewGuid(),EvidenceDigest="wrong"}),"review binds archived bytes");
Reject(()=>store.Review(r0 with{ReviewId=Guid.NewGuid(),EffectiveFrom=dates[0].AddDays(-1)}),"review cannot silently broaden requested temporal scope");
Reject(()=>store.Review(r0 with{ReviewId=Guid.NewGuid(),Unsupported=[r0.Supported[0]]}),"contradictory claims rejected");
Reject(()=>store.ReadReview("../escape"),"review path traversal blocked");
var rule=new VerifiedAvailabilityRule("test",AvailabilityPrecision.TimeWindow,at.AddHours(-2),at,"ConservativeUpperBound-v1",r0.ReviewDigest,dates[0],dates[^1]);
Check(EvidenceAcquisitionStore.Resolve(rule,r0,dates[100]).AvailableAt==at,"time window chooses reviewed upper bound, never earliest time");
Check(EvidenceAcquisitionStore.Resolve(rule with{Precision=AvailabilityPrecision.ExactTimestamp},r0,dates[100]).AvailableAt is null,"coarse evidence cannot become exact timestamp");
Check(EvidenceAcquisitionStore.Resolve(rule with{AvailabilityResolutionPolicyVersion="earliest"},r0,dates[100]).AvailableAt is null,"unapproved resolution policy blocked");
Check(EvidenceAcquisitionStore.Resolve(rule,r0,dates[0].AddDays(-1)).AvailableAt is null,"availability policy is temporal");
Check(EvidenceAcquisitionStore.Resolve(rule with{Precision=AvailabilityPrecision.Unknown},r0,dates[100]).AvailableAt is null,"unknown remains unknown");
var idA=Guid.NewGuid();var idB=Guid.NewGuid();var idC=Guid.NewGuid();var auth=Guid.NewGuid();
var membership=new[]{new UniverseIdentityFact(idA,"A","Listing",dates[0],null,at.AddYears(-1),"a",auth),
    new UniverseIdentityFact(idB,"B","Listing",dates[0],null,at.AddYears(-1),"b",auth),
    new UniverseIdentityFact(idB,"B","Delisting",dates[100],null,at.AddYears(-1),"d",auth),
    new UniverseIdentityFact(idC,"C","Listing",dates[100],null,at.AddYears(-1),"c",auth)};
var u=RealDataApproval.ReconstructUniverse(dates[50],at,2,membership);
Check(u.Members.SequenceEqual(new[]{idA,idB}.Order())&&u.DelistedHistoricalCount==1,"survivorship fixture retains later-delisted B and excludes future C");
Check(RealDataApproval.ReconstructUniverse(dates[110],at,2,membership).Members.SequenceEqual(new[]{idA,idC}.Order()),"historical eligibility changes at effective date");
Check(RealDataApproval.ReconstructUniverse(dates[50],at,null,membership).CoverageStatus!="COMPLETE","unknown denominator never reports full coverage");
Check(!RealDataApproval.PriceCompatible("raw-v1","adjusted-v1",new("v1","raw-v1","raw-v1",true,r0.ReviewDigest)),"corporate action raw and adjusted bases cannot mix");
Check(RealDataApproval.PriceCompatible("raw-v1","raw-v1",new("v1","raw-v1","raw-v1",true,r0.ReviewDigest)),"explicit compatible basis contract");
var evaluator=new RealDataApproval(evidenceRoot,authorityRoot);
RealDatasetSubmission Review(RealDatasetSubmission s)=>s with{Review=new(RealDataApproval.InputDigest(s),AuthorityStatus.Approved,at,"fixture reviewer","SIMULATED dataset review")};
var submission=Review(new(source,domainBindings.ToArray(),Enum.GetValues<EvidenceCode>(),null));
var approved=evaluator.Evaluate(submission);
Check(approved.FullApproval is not null&&approved.FullApproval.ApprovedWeight==100,"all six reviewed components create full dataset receipt in fixture");
Check(evaluator.Evaluate(submission with{Review=null}).ComponentApprovals.Length==0,"dataset receipt cannot be implicit");
Check(evaluator.Evaluate(submission with{Review=submission.Review! with{SubmissionDigest="wrong"}}).FullApproval is null,"approval binds exact input");
foreach(var domain in Enum.GetValues<EvidenceDomain>())
{
    var changed=Review(submission with{DomainBindings=domainBindings.Where(b=>b.Domain!=domain).ToArray()});
    Check(evaluator.Evaluate(changed).FullApproval is null,"full dataset blocked without "+domain);
}
var partialSource=source with{Dataset=d with{Facts=d.Facts.Where(f=>f.DatasetType!=HistoricalDatasetType.Institutional).ToImmutableArray()}};
var partial=Review(submission with{Source=partialSource,DomainBindings=domainBindings.Select(b=>b with{SubjectDigest=RealDataApproval.Subject(partialSource.Dataset,b.Domain)}).ToArray()});
var partialApproval=evaluator.Evaluate(partial);
Check(partialApproval.ComponentApprovals.Any(c=>c.EvidenceCode==EvidenceCode.Trend)&&partialApproval.FullApproval is null&&
    !partialApproval.ComponentApprovals.Any(c=>c.EvidenceCode==EvidenceCode.Institutional),"Trend approval independent of missing institutional data; full remains blocked");
var ids=d.Universes[0].Members;
var outcomes=new OutcomeDataset("fixture-labels","fixture-revision-v1",at,dates,ids.Select(id=>new OutcomeSeries(id,d.PricePolicy.PriceBasisVersion,
    d.Facts.Where(f=>f.DatasetType==HistoricalDatasetType.Price&&f.SourceInstrumentKey==id.ToString()).Select(f=>new OutcomeClose(f.TradeDate,f.Value.Close!.Value,f.AvailableAt,EvidenceStatus.Available)).ToImmutableArray())).ToImmutableArray());
var protocol=new ResearchProtocol("fixture-protocol",[new("F1",new(dates[130],dates[132]),new(dates[135],dates[137]),new(dates[140],dates[142]))],[1],3,2);
var template=new ResearchRequest(Guid.NewGuid(),at,V3CandidateCalculator.Version,"fixture-model","CandidateFeatures-v1",fixture.Parameters,protocol,[],outcomes,[],[],false,[]);
var baseline=new RealBaselineStore(baselineRoot);
var runPath=baseline.Execute(submission,template,evidenceRoot,authorityRoot,new FixedClock(at.AddSeconds(1)));
var report=baseline.Replay(template.ResearchRunId);
Check(report.Evidence.Length==6&&report.Diagnostics.Length>6&&report.Metrics.Length>0,"baseline executes frozen features and diagnostics using existing Candidate-v1");
Check(report.V3PromotionStatus=="NOT_PROMOTED"&&report.V3ThresholdStatus=="UNRESOLVED"&&report.Phase5Status=="LOCKED","baseline never unlocks model promotion or trading");
Reject(()=>baseline.Execute(submission,template with{ResearchRunId=Guid.NewGuid(),EvaluateOos=true},evidenceRoot,authorityRoot,clock),"baseline cannot expose OOS");
Reject(()=>baseline.Execute(submission,template with{ResearchRunId=Guid.NewGuid(),Outcomes=outcomes with{Series=outcomes.Series.Select(s=>s with{Closes=s.Closes.Select(c=>c with{Close=c.Close+1}).ToImmutableArray()}).ToImmutableArray()}},evidenceRoot,authorityRoot,clock),"labels cannot bypass approved source provenance");
var componentTemplate=template with{ResearchRunId=Guid.NewGuid()};
baseline.Execute(partial,componentTemplate,evidenceRoot,authorityRoot,new FixedClock(at.AddSeconds(1)));
var componentReport=baseline.Replay(componentTemplate.ResearchRunId);
Check(componentReport.Evidence.All(x=>x.FullEvidence100 is null)&&componentReport.Metrics.All(m=>m.Variable!="FullEvidence100"&&!m.Variable.StartsWith("Ablation.")),"component research never emits fake full score or ablation");
var current=authorities.Read(domainBindings[0].AuthorityId);
authorities.Review(current.Authority.AuthorityId,current.Digest,AuthorityStatus.Superseded,at.AddSeconds(2),"fixture reviewer","superseded");
Check(evaluator.Evaluate(submission).FullApproval is null,"superseded domain authority blocks new approval");
Check(baseline.Replay(template.ResearchRunId).Run.OutputDigest==report.Run.OutputDigest,"historical baseline replay survives later authority supersession");
var rawPath=Path.Combine(evidenceRoot,"evidence","authority",raw.EvidenceId.ToString("N"),"raw.bin");File.AppendAllText(rawPath,"tamper");
Reject(()=>store.Replay(plan.AcquisitionRunId),"archive byte tampering rejected");
File.AppendAllText(Path.Combine(runPath,"frozen-evidence.json")," ");
Reject(()=>baseline.Replay(template.ResearchRunId),"frozen evidence file tampering rejected");
Console.WriteLine($"V3 real-data contract checks passed: {count}; simulated fixtures only, not real research evidence.");

sealed class FixedClock(DateTimeOffset at):TimeProvider {public override DateTimeOffset GetUtcNow()=>at;}
sealed class FixtureTransport:HttpMessageHandler
{
    public const string Body="SIMULATED historical source specification. NOT REAL AUTHORITY EVIDENCE.";
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken)=>Task.FromResult(
        new HttpResponseMessage(request.RequestUri!.AbsolutePath=="/failure"?HttpStatusCode.ServiceUnavailable:HttpStatusCode.OK)
        {RequestMessage=request,Content=new StringContent(request.RequestUri.AbsolutePath=="/empty"?"":Body,Encoding.UTF8,"text/plain")});
}
