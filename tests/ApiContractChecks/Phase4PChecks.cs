using System.Net;
using System.Text.Json;
using WorkJournal.Application.Research;
using WorkJournal.Web.Models.HistoricalResearch.Components;
using WorkJournal.Web.Services.HistoricalResearch;
using WorkJournal.Web.Services.Research;
public static class Phase4PChecks
{
    private sealed class Handler(Func<int,HttpResponseMessage> respond):HttpMessageHandler
    {public int Calls;protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token){token.ThrowIfCancellationRequested();return Task.FromResult(respond(++Calls));}}
    public static async Task Run(HttpClient http,Action<bool,string> check)
    {
        var now=DateTimeOffset.UtcNow;var sha=new string('a',40);var root=Path.Combine(Path.GetTempPath(),"phase4p-"+Guid.NewGuid().ToString("N"));
        var plan=new CapturePlan("","TWSE","Price","2330",new(2026,9,1),new(2026,9,1),new(2026,9,30),"https://www.twse.com.tw/rwd/zh/afterTrading/STOCK_DAY?date=20260901&stockNo=2330&response=json","HistoricalNormalized-v1",sha,true,30,"fixture-retention");plan=plan with{Id=ForwardCaptureStore.PlanId(plan)};
        var rights=new[]{"ACQUIRE","STORE","RESEARCH","REDISTRIBUTE"}.Select(p=>new CapturePermission(plan.Id,p,"GRANTED",now.AddHours(-1),now.AddHours(2),"independent-reviewer",ResearchMath.Digest(p),30,"")).Select(p=>p with{Digest=ResearchMath.Digest(p)}).ToArray();
        var scope=new PilotScope("","TWSE",["2330"],["Price"],plan.CoverageStart,plan.CoverageEnd,now.AddHours(-1),now.AddHours(3),140,5,"TWSE",10,10000000,100000,now.AddHours(2),"requester",[plan],rights.Select(p=>p.Digest).ToArray());scope=scope with{Id=PilotScopeRules.Id(scope)};
        PilotApproval Approval(PilotScope s){var p=new PilotApproval(s.Id,"isolated-test","requester","reviewer",now.AddMinutes(-1),s.ExpiresAt,"APPROVED",ResearchMath.Digest("fixture-evidence"),"");return p with{Digest=ResearchMath.Digest(p)};}
        var approval=Approval(scope);var authorization=new PilotAuthorization(rights,[approval]);var trust=new PilotTrust("isolated-test",true,rights.Select(x=>x.Digest).ToArray(),[approval.Digest],[]);
        var body=JsonSerializer.Serialize(new{stat="OK",date="20260901",fields=new[]{"日期","成交股數","成交金額","開盤價","最高價","最低價","收盤價","漲跌價差"},data=new[]{new[]{"115/09/01","1000","100000","100","110","90","105","5"}}});
        HttpResponseMessage Ok()=>new(HttpStatusCode.OK){Content=new StringContent(body)};
        var handler=new Handler(_=>Ok());var worker=new RealAcquisitionWorker(root,()=>trust,null,handler);var run=ResearchMath.Digest("run-one");
        void Reject(Action action,string name){try{action();}catch(Exception e) when(e is IOException or InvalidDataException or InvalidOperationException or ArgumentException){check(true,name);return;}throw new Exception("Expected rejection: "+name);}
        check(PilotScopeRules.Blockers(scope,authorization,trust,now,"ACQUIRE","STORE","RESEARCH","REDISTRIBUTE").Length==0,"4P independent purpose receipts with exact scope");
        check(PilotScopeRules.Blockers(scope,authorization,trust with{RevokedDigests=[rights[0].Digest]},now,"ACQUIRE").Length>0,"4P revocation overrides pinned rights");
        check(PilotScopeRules.Blockers(scope,authorization,trust with{ApprovalDigests=[]},now,"ACQUIRE").Length>0,"4P untrusted self assertion rejected");
        check(PilotScopeRules.Blockers(scope,authorization,trust,scope.ExpiresAt,"STORE").Length>0,"4P expiration fails closed");
        check(PilotScopeRules.Blockers(scope,authorization,trust with{Isolated=false},now,"ACQUIRE").Length>0,"4P isolated environment authorization required");
        var modified=scope with{MaxRequests=11};Reject(()=>PilotScopeRules.Validate(modified),"4P mutated scope checksum rejected");modified=modified with{Id=PilotScopeRules.Id(modified)};
        check(PilotScopeRules.Blockers(modified,authorization,trust,now,"ACQUIRE").Length>0,"4P resealed expanded scope needs new independent approval");
        var badPlan=plan with{SourceUrl=plan.SourceUrl+"&extra=unapproved"};badPlan=badPlan with{Id=ForwardCaptureStore.PlanId(badPlan)};var badScope=scope with{Requests=[badPlan]};badScope=badScope with{Id=PilotScopeRules.Id(badScope)};
        Reject(()=>PilotScopeRules.Validate(badScope),"4P endpoint exact query allowlist before network");
        var absent=new RealAcquisitionWorker(root,()=>trust with{PermissionDigests=[]},null,handler);
        check((await absent.Run(scope,authorization,run,"ACQUIRE_ONCE")).Status=="BLOCKED"&&handler.Calls==0&&!Directory.Exists(root),"4P unauthorized request no network or archive");
        foreach(var mode in new[]{"PLAN","DRY_RUN"})check((await worker.Run(scope,authorization,run,mode)).Status=="READY_NO_IO"&&handler.Calls==0&&!Directory.Exists(root),"4P "+mode+" without IO");
        var completed=await worker.Run(scope,authorization,run,"ACQUIRE_ONCE");
        check(completed.Status=="COMPLETED_REVIEW_REQUIRED"&&completed.Results.Single().Facts==1&&handler.Calls==1,"4P isolated HTTP capture -> quality -> existing scheduler ETL");
        check(completed.EvidenceClass=="Synthetic Fixture"&&completed.Results.All(x=>x.EvidenceClass=="Synthetic Fixture"),"4P fixtures never real acquisition evidence");
        check((await worker.Run(scope,authorization,run,"ACQUIRE_ONCE")).Results.Single().Digest==completed.Results.Single().Digest&&handler.Calls==1,"4P duplicate run no second request");
        foreach(var mode in new[]{"REPLAY","VALIDATE"})check((await worker.Run(scope,authorization,run,mode)).Results.Single().Digest==completed.Results.Single().Digest,"4P "+mode+" verifies original raw/capture/ETL");
        // Crash after immutable raw and scheduler commit, before final orchestration receipt.
        var key=ResearchMath.Digest(new{Scope=scope.Id,Run=run,Plan=plan.Id});File.Delete(Path.Combine(root,"pilot-acquisition",key+".result.json"));
        check((await new RealAcquisitionWorker(root,()=>trust,null,handler).Run(scope,authorization,run,"ACQUIRE_ONCE")).Results.Single().Digest==completed.Results.Single().Digest&&handler.Calls==1,"4P restart recovery reuses raw and original ETL receipt");
        async Task Failure(Handler h,string label){var r=await new RealAcquisitionWorker(root+label,()=>trust,null,h).Run(scope,authorization,run,"ACQUIRE_ONCE");check(r.Results.Single().Status=="FAILED"&&h.Calls==1,"4P "+label+" rejected without retry");}
        await Failure(new(_=>new(HttpStatusCode.Redirect){Headers={Location=new Uri("https://example.com/")}}),"redirect");
        await Failure(new(_=>new(HttpStatusCode.OK){Content=new StringContent(new string('x',100001))}),"oversized");
        await Failure(new(_=>new(HttpStatusCode.TooManyRequests)),"provider-quota");
        await Failure(new(_=>new(HttpStatusCode.Forbidden)),"permission-denied");
        var malformed=new Handler(_=>new(HttpStatusCode.OK){Content=new StringContent("not-json")});var schemaWorker=new RealAcquisitionWorker(root+"schema",()=>trust,null,malformed);
        var schema=await schemaWorker.Run(scope,authorization,run,"ACQUIRE_ONCE");check(schema.Results.Single().Status=="QUARANTINED"&&schema.Results.Single().Facts==0&&!Directory.Exists(Path.Combine(root+"schema","pilot-scheduler")),"4P schema drift raw retained and quarantined before Silver");
        check((await schemaWorker.Run(scope,authorization,run,"REPLAY")).Results.Single().Digest==schema.Results.Single().Digest,"4P schema quarantine deterministic raw replay");
        var transient=new Handler(n=>n==1?new(HttpStatusCode.ServiceUnavailable):Ok());
        check((await new RealAcquisitionWorker(root+"retry",()=>trust,null,transient).Run(scope,authorization,run,"ACQUIRE_ONCE")).Results.Single().Status=="SUCCEEDED"&&transient.Calls==2,"4P bounded retry after transient provider failure");
        var limited=scope with{MaxRequests=1};limited=limited with{Id=PilotScopeRules.Id(limited)};var limitApproval=Approval(limited);
        var limitAuth=authorization with{Approvals=[limitApproval]};var limitTrust=trust with{ApprovalDigests=[limitApproval.Digest]};var limitHandler=new Handler(_=>Ok());
        await new RealAcquisitionWorker(root+"limit",()=>limitTrust,null,limitHandler).Run(limited,limitAuth,run,"ACQUIRE_ONCE");
        var exceeded=await new RealAcquisitionWorker(root+"limit",()=>limitTrust,null,limitHandler).Run(limited,limitAuth,ResearchMath.Digest("second-run"),"ACQUIRE_ONCE");
        check(exceeded.Results.Single().Status=="FAILED"&&limitHandler.Calls==1,"4P quota persists across worker restart and run IDs");
        var lowStorage=scope with{StorageLimitBytes=scope.MaximumResponseBytes};lowStorage=lowStorage with{Id=PilotScopeRules.Id(lowStorage)};var storageApproval=Approval(lowStorage);var storageHandler=new Handler(_=>Ok());
        var storageResult=await new RealAcquisitionWorker(root+"storage",()=>trust with{ApprovalDigests=[storageApproval.Digest]},null,storageHandler).Run(lowStorage,authorization with{Approvals=[storageApproval]},run,"ACQUIRE_ONCE");
        check(storageResult.Results.Single().Status=="FAILED"&&storageHandler.Calls==0,"4P complete archive storage budget checked before request");
        var changedTrust=trust;var revoking=new Handler(_=>{changedTrust=trust with{RevokedDigests=[rights[0].Digest]};return Ok();});
        check((await new RealAcquisitionWorker(root+"revoked",()=>changedTrust,null,revoking).Run(scope,authorization,run,"ACQUIRE_ONCE")).Results.Single().Status=="FAILED"&&!Directory.Exists(Path.Combine(root+"revoked","forward-capture")),"4P revocation during network prevents archive admission");
        var circuitHandler=new Handler(_=>new(HttpStatusCode.ServiceUnavailable));var circuitWorker=new RealAcquisitionWorker(root+"circuit",()=>trust,null,circuitHandler);
        await circuitWorker.Run(scope,authorization,run,"ACQUIRE_ONCE");await circuitWorker.Run(scope,authorization,ResearchMath.Digest("circuit-second"),"ACQUIRE_ONCE");
        check(circuitHandler.Calls==3,"4P persistent circuit breaker prevents next run after three failures");
        var secondPlan=plan with{Instrument="2317",SourceUrl=plan.SourceUrl.Replace("2330","2317")};secondPlan=secondPlan with{Id=ForwardCaptureStore.PlanId(secondPlan)};
        var secondRights=rights.Select(x=>x with{PlanId=secondPlan.Id,Digest=""}).Select(x=>x with{Digest=ResearchMath.Digest(x)}).ToArray();
        var multi=scope with{Instruments=["2330","2317"],Requests=[plan,secondPlan],RightsDigests=rights.Concat(secondRights).Select(x=>x.Digest).ToArray()};multi=multi with{Id=PilotScopeRules.Id(multi)};var multiApproval=Approval(multi);
        var multiTrust=trust with{PermissionDigests=multi.RightsDigests,ApprovalDigests=[multiApproval.Digest]};var multiAuth=new PilotAuthorization(rights.Concat(secondRights).ToArray(),[multiApproval]);var multiHandler=new Handler(n=>n==1?Ok():new(HttpStatusCode.Forbidden));
        var partialRun=await new RealAcquisitionWorker(root+"partial",()=>multiTrust,null,multiHandler).Run(multi,multiAuth,run,"ACQUIRE_ONCE");
        check(partialRun.Status=="PARTIAL_OR_INCOMPLETE"&&partialRun.Results.Count(x=>x.Status=="SUCCEEDED")==1&&partialRun.Results.Count(x=>x.Status=="FAILED")==1,"4P partial provider failure retains committed first instrument");
        var stopped=new CancellationTokenSource();stopped.Cancel();try{await worker.Run(scope,authorization,ResearchMath.Digest("cancelled"),"ACQUIRE_ONCE",stopped.Token);throw new Exception("Cancellation ignored");}catch(OperationCanceledException){check(handler.Calls==1,"4P cancellation before network");}
        var real=plan with{Fixture=false};real=real with{Id=ForwardCaptureStore.PlanId(real)};var realScope=scope with{Requests=[real]};realScope=realScope with{Id=PilotScopeRules.Id(realScope)};
        Reject(()=>worker.Run(realScope,authorization,run,"ACQUIRE_ONCE").GetAwaiter().GetResult(),"4P fake transport cannot be classified real");
        var noResearch=authorization with{Permissions=rights.Where(x=>x.Purpose!="RESEARCH").ToArray()};check(PilotScopeRules.Blockers(scope,noResearch,trust,now,"RESEARCH").Length==1,"4P acquire/store do not imply research permission");
        var id=Guid.NewGuid();var evidence=Guid.NewGuid();var date=plan.RequestDate;var known=new DateTimeOffset(2026,8,1,0,0,0,TimeSpan.Zero);
        var identity=new HistoricalInstrumentIdentityRecord(id,"TWSE","2330",known,null,date.AddYears(-1),null,null,null,null,null,known,known,evidence,"source-v1");
        var session=new HistoricalTradingSession("TWSE",date,SessionKind.EmergencyClosure,false,null,null,known,known,evidence,"calendar-v1");
        var action=new HistoricalCorporateAction(id,"StockSplit",date,null,date,.5m,now.AddDays(1),evidence,"action-v1");
        var context=new PilotMarketContext([identity],[],[],[session],[action],[],[ResearchMath.Digest(identity),ResearchMath.Digest(session),ResearchMath.Digest(action)]);
        var assessment=PilotMarketContextRules.Assess(context,"TWSE","2330",date,now);
        check(assessment.InstrumentId==id&&assessment.CalendarState=="EmergencyClosure"&&assessment.Tradable==false,"4P historical identity and emergency market closure distinguished");
        check(assessment.HistoricalMember is null&&assessment.Industry is null,"4P no current membership or industry fallback");
        check(assessment.ActionDigests.Length==0&&assessment.CorporateActionState=="UNVERIFIED_OR_FUTURE_KNOWN","4P future-known adjustment excluded");
        check(PilotMarketContextRules.Assess(context with{Identities=[identity with{AvailableAt=now.AddDays(1)}]},"TWSE","2330",date,now).InstrumentId is null,"4P future-known identity cannot enter historical universe");
        var universe=new HistoricalUniverseSnapshot(Guid.NewGuid(),"TWSE",date,[id],"universe-v1",[evidence],[],known,"");universe=universe with{Digest=ResearchMath.Digest(universe)};
        var member=new HistoricalUniverseMember(universe.UniverseSnapshotId,id,"MEMBER","historical",true,false,evidence);
        InstrumentContextVersion Version(string kind,string value,string source){var v=new InstrumentContextVersion(id,kind,value,known,null,known,evidence,source,"");return v with{Digest=ResearchMath.Digest(v)};}
        var membership=Version("UNIVERSE_MEMBERSHIP",universe.UniverseSnapshotId.ToString(),universe.Digest);var industry=Version("INDUSTRY","Semiconductor",ResearchMath.Digest("classification-source"));var suspended=Version("TRADING_STATE","SUSPENDED",ResearchMath.Digest("suspension-source"));
        var normal=session with{SessionType=SessionKind.NormalTradingDay,IsTradingDay=true,OpenTime=new(9,0),CloseTime=new(13,30)};var visibleAction=action with{AvailableAt=known};
        var dated=new PilotMarketContext([identity],[universe],[member],[normal],[visibleAction],[membership,industry,suspended],new object[]{identity,universe,member,normal,visibleAction,membership,industry,suspended}.Select(ResearchMath.Digest).ToArray());
        var suspendedView=PilotMarketContextRules.Assess(dated,"TWSE","2330",date,now);
        check(suspendedView.HistoricalMember==true&&suspendedView.Tradable==false&&suspendedView.CalendarState=="NormalTradingDay"&&suspendedView.Industry=="Semiconductor","4P dated universe industry and instrument suspension separate from market closure");
        var resumed=Version("TRADING_STATE","TRADING",ResearchMath.Digest("resumption-source"));
        check(PilotMarketContextRules.Assess(dated with{Versions=[membership,industry,resumed],ReviewedDigests=dated.ReviewedDigests.Append(ResearchMath.Digest(resumed)).ToArray()},"TWSE","2330",date,now).Tradable==true,"4P verified resumption restores instrument tradability");
        check(suspendedView.ActionDigests.Length==1&&visibleAction.AdjustmentFactor==.5m,"4P visible source adjustment retained without mutating raw prices");
        var delisted=identity with{DelistingDate=date};check(PilotMarketContextRules.Assess(dated with{Identities=[delisted],ReviewedDigests=dated.ReviewedDigests.Append(ResearchMath.Digest(delisted)).ToArray()},"TWSE","2330",date,now).InstrumentId is null,"4P delisted identity excluded at effective date");
        var self=approval with{Reviewer="requester",Digest=""};self=self with{Digest=ResearchMath.Digest(self)};check(PilotScopeRules.Blockers(scope,authorization with{Approvals=[self]},trust with{ApprovalDigests=[self.Digest]},now,"ACQUIRE").Length>0,"4P even pinned requester cannot independently approve own scope");
        var parent=HistoricalDatasetStore.Read<Phase4OReport>("docs/phase4o/OperationsReport-Phase4O.json");var report=Phase4POperations.Create(parent,sha,now);Phase4POperations.Validate(report,parent);
        check(report.Counts==new PilotActualCounts(0,0,0,0,0,0,0,0,0)&&report.Scope.Instruments.Length==0&&report.Scope.HistoricalStart is null,"4P nine actual counts zero and proposed scope unapproved");
        var inflated=report with{Counts=report.Counts with{NewRawSnapshots=1}};inflated=inflated with{Digest=ResearchMath.Digest(inflated with{Digest=""})};Reject(()=>Phase4POperations.Validate(inflated,parent),"4P resealed synthetic success cannot inflate real counts");
        check((await http.GetAsync("/api/v1/data/pilot")).StatusCode==HttpStatusCode.Forbidden,"4P member forbidden");http.DefaultRequestHeaders.Add("X-Test-Role","Administrator");
        try{var response=await http.GetAsync("/api/v1/data/pilot");var json=await response.Content.ReadAsStringAsync();check(response.IsSuccessStatusCode&&response.Headers.CacheControl?.NoStore==true&&!json.Contains("bodyBase64",StringComparison.OrdinalIgnoreCase)&&!json.Contains(root),"4P administrator metadata no-store filtered");
            check((await http.PostAsync("/api/v1/data/pilot",new StringContent("{}"))).StatusCode==HttpStatusCode.MethodNotAllowed,"4P website cannot activate download or approval");
            check((await http.GetStringAsync("/ResearchValidation")).Contains("research-pilot.js"),"4P existing ResearchValidation uses pilot API");}
        finally{http.DefaultRequestHeaders.Remove("X-Test-Role");}
        var user=http.DefaultRequestHeaders.GetValues("X-Test-User").Single();http.DefaultRequestHeaders.Remove("X-Test-User");try{check((await http.GetAsync("/api/v1/data/pilot")).StatusCode==HttpStatusCode.Unauthorized,"4P anonymous denied");}finally{http.DefaultRequestHeaders.Add("X-Test-User",user);}
    }
}
