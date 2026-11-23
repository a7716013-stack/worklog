using System.Net;
using WorkJournal.Application.Research;
using WorkJournal.Web.Services.HistoricalResearch;
using WorkJournal.Web.Services.Research;
public static class Phase4RChecks
{
    public static async Task Run(HttpClient http,Action<bool,string> check)
    {
        var sha=new string('b',40);var now=new DateTimeOffset(2026,12,2,0,0,0,TimeSpan.Zero);
        var scope=Phase4ROperations.DraftScope(sha);
        check(scope.Instruments.Length==10&&scope.Requests.Length==30&&scope.MaxRequests==90,"4R exact 10-stock 30-request 90-attempt candidate");
        check(scope.Requests.Select(p=>p.RequestDate).Distinct().Count()==3&&scope.Requests.Select(p=>p.SourceUrl).Distinct().Count()==30,"4R three historical months with unique exact URLs");
        check(scope.Requests.All(p=>!p.Fixture&&p.RequestDate>=scope.HistoricalStart&&p.RequestDate<=scope.HistoricalEnd)&&scope.RightsDigests.Length==0,"4R proposal contains no approvals or fixture masquerading as capture");
        check(scope.Id!=Phase4QOperations.DraftScope(sha).Id&&!scope.Requests.Select(p=>p.Id).Intersect(Phase4QOperations.DraftScope(sha).Requests.Select(p=>p.Id)).Any(),"4R narrowed scope and plans cannot reuse 4Q IDs");
        check(Phase4ROperations.DraftScope(new string('c',40)).Id!=scope.Id,"4R source SHA changes candidate identity");
        foreach(var changed in new[]{scope with{MaxRequests=91},scope with{StorageLimitBytes=32L*1024*1024},scope with{RequestedBy="operator"},scope with{ForwardStart=scope.ForwardStart.AddHours(1)}})
            check(PilotScopeRules.Id(changed)!=scope.Id,"4R scope edits require new review identity");
        var emptyTrust=new PilotTrust("unaccepted",false,[],[],[]);var emptyAuth=new PilotAuthorization([],[]);
        var root=Path.Combine(Path.GetTempPath(),"phase4r-noio-"+Guid.NewGuid().ToString("N"));
        foreach(var mode in new[]{"PLAN","DRY_RUN","ACQUIRE_ONCE"}){
            var result=await new RealAcquisitionWorker(root,()=>emptyTrust).Run(scope,emptyAuth,ResearchMath.Digest("4r-run"),mode);
            check(result.Status=="BLOCKED"&&result.Results.Length==0&&!Directory.Exists(root),"4R missing gates "+mode+" fails before network/storage");
        }
        // A case-only identity change is not an independent reviewer, even when the receipt is pinned.
        var candidate=scope with{RequestedBy="operator"};candidate=candidate with{Id=PilotScopeRules.Id(candidate)};
        var approval=new PilotApproval(candidate.Id,"isolated","operator","OPERATOR",now.AddDays(-1),candidate.ExpiresAt,"APPROVED",ResearchMath.Digest("fixture-scope"),"");approval=approval with{Digest=ResearchMath.Digest(approval)};
        var rights=candidate.Requests.SelectMany(p=>new[]{"ACQUIRE","STORE"}.Select(purpose=>new CapturePermission(p.Id,purpose,"GRANTED",now.AddDays(-1),candidate.ExpiresAt,"OPERATOR",ResearchMath.Digest("fixture-right"),90,""))).Select(p=>p with{Digest=ResearchMath.Digest(p)}).ToArray();
        candidate=candidate with{RightsDigests=rights.Select(p=>p.Digest).ToArray()};candidate=candidate with{Id=PilotScopeRules.Id(candidate)};
        approval=approval with{ScopeId=candidate.Id,Digest=""};approval=approval with{Digest=ResearchMath.Digest(approval)};
        var trust=new PilotTrust("isolated",true,candidate.RightsDigests,[approval.Digest],[]);
        var blockers=PilotScopeRules.Blockers(candidate,new(rights,[approval]),trust,now,"ACQUIRE","STORE");
        check(blockers.Contains("INDEPENDENT_SCOPE_AND_ENVIRONMENT_APPROVAL_REQUIRED"),"4R case-only self scope approval rejected");
        check(PilotScopeRules.Effective(candidate,new(rights,[approval]),trust).Length==0,"4R case-only self rights approval rejected");
        var parent=HistoricalDatasetStore.Read<Phase4QReport>("docs/phase4q/OperationsReport-Phase4Q.json");
        var report=Phase4ROperations.Create(parent,sha,now);Phase4ROperations.Validate(report,parent);
        check(report.Proposal.Status=="PROPOSED_NOT_APPROVED"&&report.Rights.Purposes.All(x=>x.Status=="UNKNOWN"&&x.PermissionDigests.Length==0),"4R review notes grant no rights");
        check(report.Environment.Controls.Length==17&&report.Environment.Boundaries.Length==7&&report.Environment.AcceptedReceipt is null&&report.Environment.Controls.All(x=>x.EvidenceDigest is null),"4R actual acceptance checklist never fabricates evidence");
        check(report.RunConsent is null&&!report.Acquisition.CanAcquire&&report.Actual.RealAcquisitionRuns==0&&report.Runs.Length==0,"4R no consent or real acquisition inferred");
        check(report.CaptureVerification.Requirements.Length==12&&report.CaptureVerification.ReplayStatus=="NOT_RUN_NO_REAL_CAPTURE","4R capture verification requirements separate from replay result");
        check(report.Pit.PriorRawFacts==21&&report.Pit.NewRawFacts==0&&report.Pit.ProposedScopeDenominator is null&&report.Pit.PitEligibleObservations==0,"4R historical facts do not become pilot coverage");
        check(report.Readiness.AcquisitionReady=="BLOCKED"&&report.Readiness.DatasetEligible=="BLOCKED"&&report.Readiness.ResearchReady=="BLOCKED","4R three distinct readiness gates stay blocked");
        check(report.FormulaDigest==parent.FormulaDigest&&report.SamplePolicy==parent.SamplePolicy&&report.Baseline=="NOT_RUN"&&report.Oos=="NOT_RUN"&&report.Phase5=="LOCKED","4R research policy unchanged");
        foreach(var bad in new[]{report with{Actual=report.Actual with{RealAcquisitionRuns=1}},report with{Readiness=report.Readiness with{ResearchReady="READY"}},report with{Proposal=report.Proposal with{MaximumAttempts=900}},report with{CaptureVerification=report.CaptureVerification with{ReplayStatus="PASS"}},report with{Environment=report.Environment with{Status="PASS"}}}){
            try{Phase4ROperations.Validate(bad with{Digest=ResearchMath.Digest(bad with{Digest=""})},parent);throw new Exception("resealed tamper accepted");}catch(InvalidDataException){check(true,"4R resealed fabricated evidence rejected");}
        }
        const string path="/api/v1/data/pilot/review";
        check((await http.GetAsync(path)).StatusCode==HttpStatusCode.Forbidden,"4R member denied");http.DefaultRequestHeaders.Add("X-Test-Role","Administrator");
        try{
            var response=await http.GetAsync(path);var json=await response.Content.ReadAsStringAsync();
            check(response.IsSuccessStatusCode&&response.Headers.CacheControl?.NoStore==true&&json.Contains("PROPOSED_NOT_APPROVED")&&!json.Contains("bodyBase64",StringComparison.OrdinalIgnoreCase)&&!json.Contains("connectionString",StringComparison.OrdinalIgnoreCase),"4R admin metadata no-store and sanitized");
            check((await http.PostAsync(path,new StringContent("{}"))).StatusCode==HttpStatusCode.MethodNotAllowed,"4R no HTTP acquisition/approval operation");
            check((await http.GetStringAsync("/ResearchValidation")).Contains("research-pilot-review.js"),"4R integrated existing ResearchValidation");
            var file=Path.Combine(AppContext.BaseDirectory,"Research","phase4r-state.json");var original=File.ReadAllBytes(file);
            try{File.WriteAllText(file,"{}");var bad=await http.GetAsync(path);check(bad.StatusCode==HttpStatusCode.ServiceUnavailable&&!(await bad.Content.ReadAsStringAsync()).Contains(file),"4R corrupt evidence fails closed without path leak");}finally{File.WriteAllBytes(file,original);}
        }finally{http.DefaultRequestHeaders.Remove("X-Test-Role");}
        var user=http.DefaultRequestHeaders.GetValues("X-Test-User").Single();http.DefaultRequestHeaders.Remove("X-Test-User");
        try{check((await http.GetAsync(path)).StatusCode==HttpStatusCode.Unauthorized,"4R anonymous denied");}finally{http.DefaultRequestHeaders.Add("X-Test-User",user);}
        var attr=typeof(WorkJournal.Api.Controllers.Phase4RController).GetCustomAttributes(typeof(Microsoft.AspNetCore.RateLimiting.EnableRateLimitingAttribute),true).Cast<Microsoft.AspNetCore.RateLimiting.EnableRateLimitingAttribute>().Single();
        check(attr.PolicyName=="ReadPolicy","4R retains tested rate-limit policy");
        var spec=WorkJournal.Api.OpenApi.ApiDocument.Create();
        check(spec["paths"]?[path]?["get"] is not null&&spec["components"]?["schemas"]?["PilotScope"]?["properties"]?["storageLimitBytes"]?["format"]?.GetValue<string>()=="int64","4R OpenAPI documents review endpoint and numeric storage budget");
    }
}
