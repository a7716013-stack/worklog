using System.Net;
using System.Text.Json.Nodes;
using WorkJournal.Application.Research;
using WorkJournal.Web.Models.HistoricalResearch;
using WorkJournal.Web.Services.HistoricalResearch;
public static class Phase4JChecks
{
    public static async Task Run(HttpClient http,Action<bool,string> check)
    {
        var paths=new[]{"/api/v1/authority/sources","/api/v1/authority/acquisition-plan","/api/v1/authority/acquisition-attempts/missing","/api/v1/research/components/Trend/readiness"};
        http.DefaultRequestHeaders.Remove("X-Test-User");
        foreach(var path in paths){var a=await http.GetAsync(path);check(a.StatusCode==HttpStatusCode.Unauthorized&&a.Headers.Contains("X-Trace-Id"),"Phase4J authentication "+path);}
        http.DefaultRequestHeaders.Add("X-Test-User","alice");
        async Task<JsonNode> Get(string path,int expected=200)
        {var a=await http.GetAsync(path);check((int)a.StatusCode==expected&&a.Headers.Contains("X-Trace-Id"),"Phase4J HTTP "+expected+" "+path);var j=JsonNode.Parse(await a.Content.ReadAsStringAsync())!;if(expected>=400)check(j["code"] is not null&&j["traceId"] is not null,"Phase4J ProblemDetails");return j;}
        var sources=await Get(paths[0]+"?pageSize=200");check(sources["sources"]!["items"]!.AsArray().Count==13,"13 archived source versions");
        var plan=await Get(paths[1]+"?pageSize=200");var tasks=plan["tasks"]!["items"]!.AsArray();
        check(tasks.Select(t=>t!["claimKey"]!.GetValue<string>()).Distinct().Count()==20,"all twenty claims scheduled");
        check(tasks.Select(t=>t!["taskId"]!.GetValue<string>()).Distinct().Count()==tasks.Count,"task dedup claim subject source version");
        var id=tasks.First(t=>t!["attempts"]!.GetValue<int>()>0)!["taskId"]!.GetValue<string>();
        var history=await Get("/api/v1/authority/acquisition-attempts/"+id);
        check(history["attempts"]!.AsArray().Count>0&&!history.ToJsonString().Contains("raw.bin")&&!history.ToJsonString().Contains("C:\\"),"sanitized existing ledger attempts");
        await Get(paths[2],404);await Get(paths[1]+"?claimKey=unknown",400);await Get(paths[0]+"?page=0",400);
        await Get("/api/v1/research/components/unknown/readiness",404);
        var trend=await Get(paths[3]);check(trend["build"]!["factsCount"]!.GetValue<int>()==21&&trend["build"]!["requestedObservations"]!.GetValue<int>()==1,"build facts distinct from observations");
        var momentum=await Get("/api/v1/research/components/Momentum/readiness");check(momentum["build"]!["factsCount"] is null&&momentum["build"]!["approvalId"] is null,"independent component unknown not zero or inherited approval");
        var post=await http.PostAsync(paths[1],new StringContent("{}"));check(post.StatusCode==HttpStatusCode.MethodNotAllowed,"no acquisition HTTP writes");
        var p=new Phase4IReadService(Path.Combine(AppContext.BaseDirectory,"Research","phase4i-state.json")).Read();
        var r=HistoricalDatasetStore.Read<Phase4JReport>(Path.Combine(AppContext.BaseDirectory,"Research","phase4j-state.json"));Phase4JProjection.Validate(r,p);
        void Reject(Action action,string name){try{action();}catch(Exception e) when(e is IOException or InvalidDataException or ArgumentException or InvalidOperationException){check(true,name);return;}throw new Exception("Expected rejection "+name);}
        Reject(()=>Phase4JProjection.Validate(r with{Digest="tamper"},p),"supply report tamper");
        Reject(()=>Phase4JProjection.Validate(Phase4JProjection.Seal(r with{Sources=r.Sources.Select(s=>s with{SourceVersion="tamper"}).ToArray()}),p),"resealed source version mismatch");
        Reject(()=>Phase4JProjection.Validate(Phase4JProjection.Seal(r with{Builds=r.Builds.Select(b=>b with{ApprovedResearchSamples=21}).ToArray()}),p),"cannot convert facts into approved samples");
        check(Phase4JProjection.Plan(p,r.Sources.Concat(r.Sources).ToArray(),r.Attempts).Length==r.Plan.Length,"duplicate planner inputs merge");
        var s=r.Sources[0];var impact=Phase4JProjection.Invalidate(r,s.SourceId,s.SourceVersion,"REVOKED");
        var revoked=Phase4JProjection.Create(p,r.Attempts,impact,r.GeneratedAt);Phase4JProjection.Validate(revoked,p);
        check(revoked.Builds.All(b=>b.ReplayStatus=="REEVALUATION_REQUIRED"&&b.ApprovalId is null&&b.ApprovedResearchSamples==0&&b.ResearchStatus=="NOT_RUN"),"source revocation invalidates dependent readiness without granting authority");
        check(r.Sources.All(s=>s.PublicationEvidenceCapability=="UNKNOWN"&&s.RevisionEvidenceCapability=="UNKNOWN")&&p.Validation.Observations.All(o=>o.AvailableAt is null),"capture metadata cannot establish historical capability");
        var t=r.Plan.First();var now=DateTimeOffset.UtcNow;
        Reject(()=>AcquisitionSafety.CheckBudget(Enumerable.Range(0,3).Select(_=>new AcquisitionReservation(t.TaskId,"provider",now,t.SourceVersion)).ToArray(),t,"provider",now),"bounded persisted retries");
        Reject(()=>AcquisitionSafety.CheckBudget(Enumerable.Range(0,20).Select(n=>new AcquisitionReservation(n.ToString(),"provider",now,t.SourceVersion)).ToArray(),t,"provider",now),"provider hourly quota");
        AcquisitionSafety.CheckBudget([new("other","provider",now.AddHours(-2),t.SourceVersion)],t,"provider",now);check(true,"expired provider reservation does not consume current quota");
        foreach(var url in new[]{"http://www.twse.com.tw/","https://127.0.0.1/","https://www.twse.com.tw:444/","https://user@www.twse.com.tw/","https://evil.test/","https://www.twse.com.tw/#fragment"})Reject(()=>AcquisitionSafety.ValidateUrl(url),"SSRF URL rejected "+url);
        foreach(var ip in new[]{"127.0.0.1","10.1.2.3","169.254.169.254","172.16.0.1","192.168.1.1","100.64.0.1","::1","::ffff:127.0.0.1","fc00::1","fe80::1"})check(!AcquisitionSafety.PublicAddress(IPAddress.Parse(ip)),"private DNS rejected "+ip);
        check(AcquisitionSafety.PublicAddress(IPAddress.Parse("8.8.8.8")),"public address permitted");
        check(AcquisitionSafety.Classify("HTTP_404")=="SOURCE_UNAVAILABLE"&&AcquisitionSafety.Classify("HTTP_403")=="MANUAL_ACTION_REQUIRED"&&AcquisitionSafety.Classify("HTTP_429")=="PROVIDER_QUOTA","unavailable unauthorized quota distinct from missing data");
        check(AcquisitionSafety.Retryable("PROVIDER_TIMEOUT")&&!AcquisitionSafety.Retryable("PROVIDER_QUOTA")&&!AcquisitionSafety.Retryable("SOURCE_UNAVAILABLE"),"bounded retry classification");
        var root=Path.Combine(Path.GetTempPath(),"phase4j-timeout-"+Guid.NewGuid());
        using var client=new HttpClient(new TimeoutHandler());using var cancel=new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        var task=new EvidenceAcquisitionTask(Guid.NewGuid(),"TWSE","Price",EvidenceDomain.Availability,new(2026,9,30),new(2026,9,30),AvailabilityEvidenceType.OfficialSpecification,AcquisitionStatus.Pending,0,null,"","https://www.twse.com.tw/",null,"");
        var result=await new EvidenceAcquisitionStore(root).Acquire(new(Guid.NewGuid(),"TWSE",task.TargetPeriodStart,task.TargetPeriodEnd,[task]),client,TimeProvider.System,cancel.Token);
        check(result.CapturedArtifacts.Length==0&&result.FailedArtifacts.Length==1,"provider timeout persisted no capture");
        using(var transport=AcquisitionSafety.Transport())check(!transport.AllowAutoRedirect&&!transport.UseProxy&&!transport.UseCookies,"production transport disables redirects proxies and credentials");
        using(var lease=AcquisitionSafety.Lease(root))Reject(()=>{using var other=AcquisitionSafety.Lease(root);},"cross-process acquisition concurrency one");
        using(var oversized=new HttpClient(new OversizedHandler()))
        {
            var large=await new EvidenceAcquisitionStore(root).Acquire(new(Guid.NewGuid(),"TWSE",task.TargetPeriodStart,task.TargetPeriodEnd,[task]),oversized,TimeProvider.System);
            check(large.CapturedArtifacts.Length==0&&large.FailedArtifacts.Single().Reason=="ARTIFACT_TOO_LARGE","download size capped before archive");
        }
        var supply=Path.Combine(root,"supply-chain");Directory.CreateDirectory(supply);
        HistoricalDatasetStore.WriteNew(Path.Combine(supply,"revoked.impact.json"),impact[0]);
        Reject(()=>AcquisitionSafety.RequireCurrentSources(root),"current source invalidation blocks controlled execution");
        http.DefaultRequestHeaders.Remove("X-Test-User");http.DefaultRequestHeaders.Add("X-Test-User","bob");var bob=await Get(paths[1]+"?pageSize=200");check(bob.ToJsonString()==plan.ToJsonString(),"shared nonpersonal published research has same projection across users");
        http.DefaultRequestHeaders.Remove("X-Test-User");http.DefaultRequestHeaders.Add("X-Test-User","alice");
    }
    private sealed class TimeoutHandler:HttpMessageHandler
    {protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token){await Task.Delay(10000,token);return new(HttpStatusCode.OK);}}
    private sealed class OversizedHandler:HttpMessageHandler
    {protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token){var content=new ByteArrayContent([1]);content.Headers.ContentLength=AcquisitionSafety.MaximumBytes+1;return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){RequestMessage=request,Content=content});}}
}
