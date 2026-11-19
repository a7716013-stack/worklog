using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using WorkJournal.Application.Research;
using WorkJournal.Web.Models.HistoricalResearch;
using WorkJournal.Web.Services.HistoricalResearch;
using WorkJournal.Web.Services.Research;
public static class Phase4OChecks
{
    public static async Task Run(HttpClient http,Action<bool,string> check)
    {
        var now=DateTimeOffset.UtcNow;var source=new string('a',40);var root=Path.Combine(Path.GetTempPath(),"phase4o-"+Guid.NewGuid().ToString("N"));var store=new ForwardCaptureStore(root);
        SourceArchive Raw(string close="105",DateTimeOffset? at=null){var bytes=JsonSerializer.SerializeToUtf8Bytes(new{stat="OK",date="20260901",fields=new[]{"日期","成交股數","成交金額","開盤價","最高價","最低價","收盤價","漲跌價差"},data=new[]{new[]{"115/09/01","1000","100000","100","110","90",close,"5"}}});return new("fixture-archive","TWSE",HistoricalDatasetType.Price,"https://www.twse.com.tw/rwd/zh/afterTrading/STOCK_DAY?date=20260901&stockNo=2330&response=json",at??now,at??now,Convert.ToBase64String(bytes),Convert.ToHexString(SHA256.HashData(bytes)),"TWSE-capture-v1",true);}
        var raw=Raw();var plan=new CapturePlan("","TWSE","Price","2330",new(2026,9,1),new(2026,9,1),new(2026,9,30),raw.SourceUri,"HistoricalNormalized-v1",source,true,365,"FIXTURE_NO_AUTOMATIC_DELETION");plan=plan with{Id=ForwardCaptureStore.PlanId(plan)};
        CaptureInput Input(SourceArchive a,string key)=>new(plan.Id,ResearchMath.Digest(key),a,now,new(null,null,"UNKNOWN"));
        void Reject(Action action,string name){try{action();}catch(Exception e) when(e is IOException or InvalidDataException or InvalidOperationException or ArgumentException or FormatException or JsonException){check(true,name);return;}throw new Exception("Expected rejection: "+name);}
        var policies=Phase4OOperations.Providers();check(policies.Select(x=>x.Capabilities.Provider).SequenceEqual(OfficialDataProviderAdapter.Catalog().Select(x=>x.Provider)),"4O registry extends existing provider identities");
        check(policies.All(x=>x.Rights.Length==4&&x.Rights.All(y=>y.Status=="UNKNOWN")&&x.HealthStatus=="NOT_PROBED"),"4O distinct unknown acquire/store/research/redistribute rights");
        var realPlan=plan with{Fixture=false};realPlan=realPlan with{Id=ForwardCaptureStore.PlanId(realPlan)};
        check(ForwardCaptureStore.AuthorizationBlockers(realPlan,[],new HashSet<string>(),now).Length==2,"4O unknown acquisition/storage rights block real intake");
        var realInput=new CaptureInput(realPlan.Id,ResearchMath.Digest("real-blocked"),raw with{Synthetic=false},now,new(null,null,"UNKNOWN"));
        Reject(()=>store.Append(realPlan,realInput,[],new HashSet<string>(),now),"4O unapproved real capture rejected before IO");check(!Directory.Exists(root),"4O unauthorized intake creates no archive or download");
        var rights=new[]{"ACQUIRE","STORE"}.Select(p=>new CapturePermission(realPlan.Id,p,"GRANTED",now.AddMinutes(-1),now.AddDays(1),"fixture-reviewer","fixture-only-external-proof",365,"")).Select(p=>p with{Digest=ResearchMath.Digest(p)}).ToArray();var trusted=rights.Select(x=>x.Digest).ToHashSet();
        check(ForwardCaptureStore.AuthorizationBlockers(realPlan,rights,trusted,now).Length==0,"4O exact scoped reviewed permission fixture is assessable, not real permission");
        check(ForwardCaptureStore.AuthorizationBlockers(realPlan,rights,trusted,now,now.AddDays(-1)).Length==1,"4O later permission does not retroactively authorize an earlier acquisition");
        check(ForwardCaptureStore.AuthorizationBlockers(realPlan,rights,new HashSet<string>(),now).Length==2,"4O self asserted grants without trusted receipts rejected");
        var changed=rights[0] with{ExpiresAt=now,Digest=""};changed=changed with{Digest=ResearchMath.Digest(changed)};var expiredTrust=new HashSet<string>(trusted){changed.Digest};
        check(ForwardCaptureStore.AuthorizationBlockers(realPlan,[changed,rights[1]],expiredTrust,now).Length==1,"4O trusted expired grant fails closed");
        check(ForwardCaptureStore.AuthorizationBlockers(realPlan,rights.Select(x=>x with{PlanId=plan.Id}).ToArray(),trusted,now).Length==2,"4O source/date/universe plan-bound rights cannot be reused");
        var first=store.Append(plan,Input(raw,"first"),[],new HashSet<string>(),now);check(first.Sequence==1&&first.VersionSequence==1&&first.Publication.ClaimedPublishedAt is null,"4O original publication is not inferred from observed/retrieved");
        check(store.Append(plan,Input(raw,"first"),[],new HashSet<string>(),now).Digest==first.Digest,"4O identical idempotency replay returns original receipt");
        Reject(()=>store.Append(plan,Input(raw,"first") with{DecisionAt=now.AddDays(1)},[],new HashSet<string>(),now),"4O idempotency payload conflict rejected");
        var second=store.Append(plan,Input(raw with{ObservedAt=now.AddSeconds(1),RetrievedAt=now.AddSeconds(1)},"second"),[],new HashSet<string>(),now.AddSeconds(1));
        var third=store.Append(plan,Input(Raw("106",now.AddSeconds(2)),"third"),[],new HashSet<string>(),now.AddSeconds(2));
        var fourth=store.Append(plan,Input(Raw("105",now.AddSeconds(3)),"fourth"),[],new HashSet<string>(),now.AddSeconds(3));
        check(second.Sequence==2&&second.VersionSequence==1&&!second.RevisionDetected&&third.VersionSequence==2&&fourth.VersionSequence==3,"4O repeated bytes retain observation; revisions and reversions create new versions");
        check(store.Replay(plan).Runs[0].Digest==first.Digest&&store.Raw(plan,first.IdempotencyKey).SourceChecksum==raw.SourceChecksum,"4O earlier source snapshots remain immutable and replayable");
        check(first.RetainUntil==raw.RetrievedAt.AddDays(365),"4O explicit retention deadline, no automatic deletion");
        var folder=Path.Combine(root,"forward-capture",plan.Id);Directory.CreateDirectory(Path.Combine(folder,".pending-crash"));
        check(store.Replay(plan).Runs.Length==4,"4O interrupted unpublished capture does not become a committed version");
        Reject(()=>store.Append(plan,Input(raw,"backdated"),[],new HashSet<string>(),now),"4O backdated observation rejected");
        Reject(()=>store.Append(plan,Input(raw with{Synthetic=false},"mixed"),[],new HashSet<string>(),now),"4O real and fixture archive isolation");
        Reject(()=>store.Append(plan,Input(raw with{BodyBase64=Convert.ToBase64String("tampered"u8.ToArray())},"tampered"),[],new HashSet<string>(),now),"4O raw content digest enforced");
        Reject(()=>store.Append(plan,Input(raw,"publication") with{Publication=new(now,null,"SOURCE_CLAIM_UNREVIEWED")},[],new HashSet<string>(),now),"4O publication claim requires original evidence digest");
        var input=Input(Raw("107",now.AddSeconds(4)),"concurrent");
        var concurrent=await Task.WhenAll(Enumerable.Range(0,4).Select(_=>Task.Run(()=>{try{return store.Append(plan,input,[],new HashSet<string>(),now.AddSeconds(4)).Digest;}catch(IOException){return null;}})));
        check(concurrent.Any(x=>x is not null)&&concurrent.Where(x=>x is not null).Distinct().Count()==1&&store.Replay(plan).Runs.Length==5,"4O multi-worker idempotent capture has one immutable commit");
        var cal=new TradingSessionCalendar("fixture-calendar",[plan.RequestDate],"");cal=cal with{Digest=ResearchMath.Digest(cal)};
        var schedulerDir=Path.Combine(root,"scheduler");
        check(!(await store.ProcessFixture(plan,first.IdempotencyKey,cal,"Disabled")).Executed&&!Directory.Exists(schedulerDir),"4O disabled processing reuses gate without scheduler IO");
        check(!(await store.ProcessFixture(plan,first.IdempotencyKey,cal,"Dry-run")).Executed&&!Directory.Exists(schedulerDir),"4O dry-run has no scheduler IO");
        var processed=await store.ProcessFixture(plan,first.IdempotencyKey,cal,"Approved-enabled");check(processed.Status=="SUCCEEDED","4O isolated captured archive flows through existing scheduler and ETL");
        check((await store.ProcessFixture(plan,first.IdempotencyKey,cal,"Approved-enabled")).Status=="NO_DISPATCH","4O original successful ETL receipt prevents duplicate work");
        Reject(()=>store.ProcessFixture(realPlan,first.IdempotencyKey,cal,"Approved-enabled").GetAwaiter().GetResult(),"4O fixture dispatch cannot enable real scheduler");
        var request=ForwardCaptureStore.Request(plan,raw);var response=new OfficialDataProviderAdapter("TWSE").Transform(request,raw);
        var unknown=CaptureQuality.Evaluate(request,response,new(null,null,now,[]));check(unknown.Context.Single(c=>c.Check=="HistoricalPublication").Status=="UNKNOWN"&&unknown.Pit=="NOT_EVALUATED"&&unknown.Dataset=="NOT_APPROVED","4O quality success never invents PIT or Gold approval");
        var calendar=new HistoricalCalendar("fixture-v1",[plan.RequestDate],now.AddDays(-1),"fixture-source");
        var price=new HistoricalPricePolicy(response.Facts[0].PriceBasisVersion,"raw","fixture-actions","fixture-source","fixture-review",true);
        var context=new CaptureQualityContext(calendar,price,now,[ResearchMath.Digest(calendar),ResearchMath.Digest(price)]);
        var qualityReceipt=store.Assess(plan,first.IdempotencyKey,context);
        check(qualityReceipt.CaptureDigest==first.Digest&&store.Assess(plan,first.IdempotencyKey,context).Digest==qualityReceipt.Digest,"4O immutable quality sidecar binds exact capture and context replay");
        Reject(()=>store.Assess(plan,first.IdempotencyKey,context with{DecisionAt=now.AddDays(1)}),"4O quality cannot change frozen capture decision time");
        check(CaptureQuality.Evaluate(request,response,context).Context.Where(c=>c.Check is "TradingCalendar" or "CorporateActionPriceBasis").All(c=>c.Status=="PASS_CONTEXT_ONLY"),"4O exact reviewed calendar and price context checked without authority approval");
        var otherCalendar=calendar with{Sessions=[plan.RequestDate.AddDays(1)]};
        var isolated=new ForwardCaptureStore(Path.Combine(root,"quality-quarantine"));isolated.Append(plan,Input(raw,"first"),[],new HashSet<string>(),now);
        check((await isolated.ProcessFixture(plan,first.IdempotencyKey,cal,"Approved-enabled",context with{Calendar=otherCalendar,ReviewedContextDigests=[ResearchMath.Digest(otherCalendar),ResearchMath.Digest(price)]})).Status=="QUARANTINED"&&!Directory.Exists(Path.Combine(root,"quality-quarantine","scheduler")),"4O context quality quarantine prevents scheduler/Silver admission");
        check(CaptureQuality.Evaluate(request,response,context with{Calendar=otherCalendar,ReviewedContextDigests=[ResearchMath.Digest(otherCalendar),ResearchMath.Digest(price)]}).Status=="QUARANTINED","4O calendar mismatch quarantines source instead of dropping rows");
        var otherPrice=price with{PriceBasisVersion="different"};
        check(CaptureQuality.Evaluate(request,response,context with{PricePolicy=otherPrice,ReviewedContextDigests=[ResearchMath.Digest(calendar),ResearchMath.Digest(otherPrice)]}).Status=="QUARANTINED","4O corporate action/price basis mismatch quarantined");
        check(CaptureQuality.Evaluate(request,response,context with{DecisionAt=now.AddMinutes(-1)}).Status=="QUARANTINED","4O later capture cannot be used for earlier decision (look-ahead/revision leakage)");
        var a=response.Facts[0] with{Unit="SHARES",PriceBasisVersion="fixture-raw"};a=a with{FactChecksum=HistoricalAvailability.FactChecksum(a)};
        var b=a with{Provider="TPEx"};b=b with{FactChecksum=HistoricalAvailability.FactChecksum(b)};
        var same=CaptureQuality.Reconcile([a],[b]);check(same.Status=="REVIEW_REQUIRED"&&same.Authority=="NOT_APPROVED","4O matching providers still require identity/authority review");
        var different=b with{Value=b.Value with{Close=106}};different=different with{FactChecksum=HistoricalAvailability.FactChecksum(different)};
        var reconciliation=CaptureQuality.Reconcile([a],[different]);check(reconciliation.Status=="QUARANTINED"&&reconciliation.Differences[0].Fields.Contains("Close")&&a.Value.Close==105&&different.Value.Close==106,"4O source differences retain both values and digests, no overwrite");
        check(CaptureQuality.Reconcile([a],[]).Differences[0].Status=="MISSING_COUNTERPART","4O missing counterpart not silently dropped");
        check(CaptureQuality.Reconcile([a,a],[b]).Differences[0].Status=="DUPLICATE_CONFLICT","4O duplicate matches are not arbitrarily selected");
        var units=b with{Unit="UNKNOWN_EXACT_SHARES"};units=units with{FactChecksum=HistoricalAvailability.FactChecksum(units)};
        check(CaptureQuality.Reconcile([a],[units]).Differences[0].Status=="INCOMPARABLE_CONTEXT","4O incomparable volume units not reconciled by assumption");
        Reject(()=>CaptureQuality.Reconcile([a],[b with{Value=b.Value with{Close=999}}]),"4O reconciliation rejects tampered normalized fact");
        var n=HistoricalDatasetStore.Read<Phase4NReport>("docs/phase4n/OperationsReport-Phase4N.json");var report=Phase4OOperations.Create(n,source,now);Phase4OOperations.Validate(report,n);
        check(report.Acquisition.ExistingTaskIds.Length==43&&report.Acquisition.Start is null&&report.Acquisition.Instruments.Length==0,"4O three-month suggestion does not fabricate scope approval");
        check(report.Capture.NewRealRows==0&&report.Capture.NewRealSources==0&&report.Capture.NewPitEligible==0&&report.BaselineStatus=="NOT_RUN"&&report.Phase5=="LOCKED","4O report distinguishes zero real outcomes from fixture success");
        Reject(()=>Phase4OOperations.Validate(report with{Capture=report.Capture with{NewRealRows=100}},n),"4O false acquisition counts rejected");
        var impactDir=Path.Combine(root,"supply-chain");Directory.CreateDirectory(impactDir);File.WriteAllText(Path.Combine(impactDir,"fixture.impact.json"),"{}");
        Reject(()=>store.Assess(plan,first.IdempotencyKey,context),"4O existing source revocation guard blocks new quality/processing");
        check(store.Replay(plan).Runs.Length==5,"4O historical archive replay survives source revocation for audit");
        File.WriteAllText(Path.Combine(folder,"00000001","input.json"),"{}");Reject(()=>store.Replay(plan),"4O altered historical archive cannot replay");
        foreach(var path in new[]{"/api/v1/data/operations","/api/v1/data/captures"})check((await http.GetAsync(path)).StatusCode==HttpStatusCode.Forbidden,"4O member cannot access management metadata");
        http.DefaultRequestHeaders.Add("X-Test-Role","Administrator");
        try{var res=await http.GetAsync("/api/v1/data/operations");var body=await res.Content.ReadAsStringAsync();check(res.IsSuccessStatusCode&&res.Headers.CacheControl?.NoStore==true&&!body.Contains("bodyBase64",StringComparison.OrdinalIgnoreCase)&&!body.Contains(root),"4O admin no-store metadata excludes raw body and local paths");
            var page=JsonNode.Parse(await http.GetStringAsync("/api/v1/data/captures"))!;check(page["totalCount"]!.GetValue<int>()==0,"4O real history excludes isolated fixture captures");
            check((await http.GetAsync("/api/v1/data/captures?pageSize=201")).StatusCode==HttpStatusCode.BadRequest,"4O bounded history pagination");
            check((await http.PostAsync("/api/v1/data/captures",new StringContent("{}"))).StatusCode==HttpStatusCode.MethodNotAllowed,"4O HTTP cannot trigger downloads or capture writes");
        }finally{http.DefaultRequestHeaders.Remove("X-Test-Role");}
        var user=http.DefaultRequestHeaders.GetValues("X-Test-User").Single();http.DefaultRequestHeaders.Remove("X-Test-User");
        try{foreach(var path in new[]{"/api/v1/data/operations","/api/v1/data/captures"})check((await http.GetAsync(path)).StatusCode==HttpStatusCode.Unauthorized,"4O anonymous management API denied");}finally{http.DefaultRequestHeaders.Add("X-Test-User",user);}
    }
}
