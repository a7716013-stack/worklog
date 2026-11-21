using System.Net;
using System.Text.Json;
using WorkJournal.Application.Research;
using WorkJournal.Web.Services.HistoricalResearch;
using WorkJournal.Web.Services.Research;
public static class Phase4QChecks
{
    private sealed class Handler(Func<HttpResponseMessage> respond):HttpMessageHandler
    {public int Calls;protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token){Calls++;return Task.FromResult(respond());}}
    public static async Task Run(HttpClient http,Action<bool,string> check)
    {
        var now=DateTimeOffset.UtcNow;var sha=new string('b',40);var root=Path.Combine(Path.GetTempPath(),"phase4q-"+Guid.NewGuid().ToString("N"));
        var draft=Phase4QOperations.DraftScope(sha);var plan=draft.Requests[0];
        check(draft.Instruments.Length==50&&draft.Requests.Length==900&&draft.RightsDigests.Length==0&&draft.Requests.All(p=>!p.Fixture),"4Q precise review draft has 50 stocks 18 months but no approvals");
        var rights=new[]{"ACQUIRE","STORE"}.Select(p=>new CapturePermission(plan.Id,p,"GRANTED",now.AddHours(-1),now.AddHours(2),"independent-reviewer",ResearchMath.Digest("fixture-right"),90,"")).Select(p=>p with{Digest=ResearchMath.Digest(p)}).ToArray();
        var scope=draft with{Instruments=[plan.Instrument],Requests=[plan],RequestedBy="operator",ForwardStart=now.AddHours(-1),ForwardEnd=now.AddHours(3),ExpiresAt=now.AddHours(2),RightsDigests=rights.Select(p=>p.Digest).ToArray()};scope=scope with{Id=PilotScopeRules.Id(scope)};
        var approval=new PilotApproval(scope.Id,"isolated-fixture","operator","independent-reviewer",now.AddHours(-1),scope.ExpiresAt,"APPROVED",ResearchMath.Digest("fixture-scope"),"");approval=approval with{Digest=ResearchMath.Digest(approval)};
        var run=ResearchMath.Digest("fixture-run");
        IsolatedEnvironmentReceipt SealEnv(IsolatedEnvironmentReceipt e)=>e with{Digest=ResearchMath.Digest(e with{Digest=""})};
        AcquisitionRunConsent SealConsent(AcquisitionRunConsent c)=>c with{Digest=ResearchMath.Digest(c with{Digest=""})};
        var env=SealEnv(new("isolated-fixture",PilotExecutionRules.StoreIdentity(root),sha,"operator","independent-reviewer",now.AddMinutes(-2),scope.ExpiresAt,"Verified Isolated Environment",PilotExecutionRules.Controls.Select(c=>new IsolatedControl(c,"PASS",ResearchMath.Digest("fixture-"+c))).ToArray(),""));
        var consent=SealConsent(new(scope.Id,run,env.Digest,"operator","ACQUIRE_ONCE",now.AddMinutes(-1),scope.ExpiresAt,ResearchMath.Digest("fixture-explicit-consent"),""));
        var trust=new PilotTrust("isolated-fixture",true,rights.Select(x=>x.Digest).ToArray(),[approval.Digest],[],[env.Digest],[consent.Digest]);
        var evidence=new PilotExecutionEvidence(env,consent);var auth=new PilotAuthorization(rights,[approval]);
        check(PilotExecutionRules.Blockers(scope,run,root,trust,evidence,now).Length==0,"4Q accepted environment and exact run authorization compose with existing scope");
        check(PilotExecutionRules.Blockers(scope,run,root,trust,null,now).Length==2,"4Q missing environment and consent fail closed");
        check(PilotExecutionRules.Blockers(scope,run,root,trust with{EnvironmentDigests=[],ExecutionDigests=[]},evidence,now).Length==2,"4Q locally sealed receipts cannot substitute external trust");
        foreach(var digest in new[]{env.Digest,consent.Digest})check(PilotExecutionRules.Blockers(scope,run,root,trust with{RevokedDigests=[digest]},evidence,now).Length>0,"4Q revocation overrides pinned execution evidence");
        check(PilotExecutionRules.Blockers(scope,run,root+"other",trust,evidence,now).Length>0,"4Q environment bound to exact storage root");
        check(PilotExecutionRules.Blockers(scope,ResearchMath.Digest("another-run"),root,trust,evidence,now).Length>0,"4Q consent cannot authorize another run");
        check(PilotExecutionRules.Blockers(scope,run,root,trust,evidence,scope.ExpiresAt).Length>0,"4Q expired environment and run consent blocked");
        foreach(var bad in new[]{env with{Reviewer="OPERATOR"},env with{EvidenceClass="Synthetic Fixture"},env with{SourceCodeSha=new string('c',40)},env with{AcceptedAt=now.AddHours(1)},env with{Controls=env.Controls.Skip(1).ToArray()},env with{Controls=env.Controls.Select((c,i)=>i==0?c with{Status="UNKNOWN"}:c).ToArray()},env with{Controls=env.Controls.Select((c,i)=>i==0?c with{EvidenceDigest="not-evidence"}:c).ToArray()}}){
            var e=SealEnv(bad);var c=SealConsent(consent with{EnvironmentDigest=e.Digest});
            check(PilotExecutionRules.Blockers(scope,run,root,trust with{EnvironmentDigests=[e.Digest],ExecutionDigests=[c.Digest]},new(e,c),now).Length>0,"4Q trusted but invalid environment still blocked");}
        foreach(var bad in new[]{consent with{Operator="other"},consent with{ScopeId=ResearchMath.Digest("other-scope")},consent with{Mode="PLAN"},consent with{AuthorizedAt=now.AddHours(1)},consent with{ExpiresAt=scope.ExpiresAt.AddHours(1)},consent with{EvidenceDigest=""}}){
            var c=SealConsent(bad);check(PilotExecutionRules.Blockers(scope,run,root,trust with{ExecutionDigests=[c.Digest]},new(env,c),now).Length>0,"4Q trusted but invalid run consent still blocked");}
        var denied=await new RealAcquisitionWorker(root,()=>trust).Run(scope,auth,run,"ACQUIRE_ONCE");
        check(denied.Status=="BLOCKED"&&denied.Blockers.Contains("EXPLICIT_RUN_AUTHORIZATION_REQUIRED")&&!Directory.Exists(root),"4Q real worker refuses rights-only acquisition before any storage or network");
        foreach(var mode in new[]{"PLAN","DRY_RUN"}){
            var result=await new RealAcquisitionWorker(root,()=>trust,executionEvidence:()=>evidence).Run(scope,auth,run,mode);
            check(result.Status=="READY_NO_IO"&&result.Results.Length==0&&!Directory.Exists(root),"4Q "+mode+" complete gates still perform no IO");}
        var fp=plan with{Fixture=true};fp=fp with{Id=ForwardCaptureStore.PlanId(fp)};
        var fr=rights.Select(p=>p with{PlanId=fp.Id,Digest=""}).Select(p=>p with{Digest=ResearchMath.Digest(p)}).ToArray();
        var fs=scope with{Requests=[fp],RightsDigests=fr.Select(x=>x.Digest).ToArray()};fs=fs with{Id=PilotScopeRules.Id(fs)};
        var fa=approval with{ScopeId=fs.Id,Digest=""};fa=fa with{Digest=ResearchMath.Digest(fa)};
        var fc=SealConsent(consent with{ScopeId=fs.Id});var ft=trust with{PermissionDigests=fs.RightsDigests,ApprovalDigests=[fa.Digest],ExecutionDigests=[fc.Digest]};
        var body=JsonSerializer.Serialize(new{stat="OK",date="20250601",fields=new[]{"日期","成交股數","成交金額","開盤價","最高價","最低價","收盤價","漲跌價差"},data=new[]{new[]{"114/06/02","1000","100000","100","110","90","105","5"}}});
        var handler=new Handler(()=>{ft=ft with{RevokedDigests=[fc.Digest]};return new(HttpStatusCode.OK){Content=new StringContent(body)};});
        var revoked=await new RealAcquisitionWorker(root,()=>ft,fixtureTransport:handler,executionEvidence:()=>new(env,fc)).Run(fs,new(fr,[fa]),run,"ACQUIRE_ONCE");
        check(handler.Calls==1&&revoked.Results.Single().Status=="FAILED"&&!Directory.Exists(Path.Combine(root,"forward-capture")),"4Q consent revocation during response prevents raw/capture admission");
        check(Directory.GetFiles(Path.Combine(root,"pilot-acquisition"),"*.execution.json").Length==1,"4Q exact environment and run receipts archived before execution");
        var parent=HistoricalDatasetStore.Read<Phase4PReport>("docs/phase4p/OperationsReport-Phase4P.json");var historical=HistoricalDatasetStore.Read<Phase4IReport>("docs/phase4i-state-3.2.2.json");
        var report=Phase4QOperations.Create(parent,historical,sha,now);Phase4QOperations.Validate(report,parent,historical);
        check(report.Rights.Length==4&&report.Rights.All(x=>x.Status=="UNKNOWN"&&x.Reviewer is null&&x.ExpiresAt is null&&x.PermissionDigests.Length==0),"4Q terms evidence never grants any of four rights");
        check(report.Pit.TotalRawFacts==historical.Counts.TotalFacts&&report.Pit.PriorRawFacts==21&&report.Pit.NewRawFacts==0,"4Q prior real facts separated from zero newly acquired facts");
        check(report.Pit.SevenGates.Length==7&&report.Pit.SevenGates.Single(x=>x.Gate=="PriceVolume").Verified==1&&report.Pit.PublicationEvidenceObservations==0&&report.Pit.ProposedScopeDenominator is null,"4Q real inspected gate counts never fabricate proposed scope denominator");
        check(report.Actual==new PilotActualCounts(0,0,0,0,0,0,0,0,0)&&report.Pit.PitEligibleObservations==0&&report.Pit.ApprovedDatasetCount==0&&!report.Acquisition.CanAcquire,"4Q actual real milestones stay zero");
        check(report.Production.Length==8&&report.Production.All(x=>x.Status=="Blocked")&&report.IsolatedEnvironment.Length==17&&report.SchedulerMode=="Disabled"&&report.Baseline=="NOT_RUN"&&report.Phase5=="LOCKED","4Q fixture and deployment cannot release production or research gates");
        foreach(var bad in new[]{report with{Actual=report.Actual with{RealAcquisitionRuns=1}},report with{Acquisition=report.Acquisition with{CanAcquire=true}},report with{Pit=report.Pit with{PitEligibleObservations=1}},report with{Rights=report.Rights.Select(x=>x with{Status="GRANTED"}).ToArray()}}){
            var sealedBad=bad with{Digest=ResearchMath.Digest(bad with{Digest=""})};try{Phase4QOperations.Validate(sealedBad,parent,historical);throw new Exception("tamper accepted");}catch(InvalidDataException){check(true,"4Q resealed false evidence rejected");}}
        const string path="/api/v1/data/pilot/readiness";
        check((await http.GetAsync(path)).StatusCode==HttpStatusCode.Forbidden,"4Q member cannot read admin evidence");http.DefaultRequestHeaders.Add("X-Test-Role","Administrator");
        try{
            var response=await http.GetAsync(path);var json=await response.Content.ReadAsStringAsync();
            check(response.IsSuccessStatusCode&&response.Headers.CacheControl?.NoStore==true&&!json.Contains("bodyBase64",StringComparison.OrdinalIgnoreCase)&&!json.Contains(root)&&!json.Contains("connectionString",StringComparison.OrdinalIgnoreCase),"4Q admin metadata filtered and no-store");
            check((await http.PostAsync(path,new StringContent("{}"))).StatusCode==HttpStatusCode.MethodNotAllowed,"4Q no web acquisition or approval route");
            check((await http.GetStringAsync("/ResearchValidation")).Contains("research-pilot-readiness.js"),"4Q existing ResearchValidation integrates readiness API");
            var file=Path.Combine(AppContext.BaseDirectory,"Research","phase4q-state.json");var original=File.ReadAllBytes(file);
            try{File.WriteAllText(file,"{}");var bad=await http.GetAsync(path);check(bad.StatusCode==HttpStatusCode.ServiceUnavailable&&!(await bad.Content.ReadAsStringAsync()).Contains(file),"4Q invalid report fails closed with sanitized 503");}finally{File.WriteAllBytes(file,original);}
        }finally{http.DefaultRequestHeaders.Remove("X-Test-Role");}
        var user=http.DefaultRequestHeaders.GetValues("X-Test-User").Single();http.DefaultRequestHeaders.Remove("X-Test-User");try{check((await http.GetAsync(path)).StatusCode==HttpStatusCode.Unauthorized,"4Q anonymous denied");}finally{http.DefaultRequestHeaders.Add("X-Test-User",user);}
        var attribute=typeof(WorkJournal.Api.Controllers.Phase4QController).GetCustomAttributes(typeof(Microsoft.AspNetCore.RateLimiting.EnableRateLimitingAttribute),true).Cast<Microsoft.AspNetCore.RateLimiting.EnableRateLimitingAttribute>().Single();
        check(attribute.PolicyName=="ReadPolicy","4Q rate limiting uses existing tested 429 policy");
    }
}
