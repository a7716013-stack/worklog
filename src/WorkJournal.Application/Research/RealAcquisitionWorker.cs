using System.Net;
using System.Security.Cryptography;
using WorkJournal.Web.Models.HistoricalResearch;
using WorkJournal.Web.Services.HistoricalResearch;
using WorkJournal.Web.Services.Research;
namespace WorkJournal.Application.Research;

public sealed record PilotTicket(string ScopeId,string PlanId,string RunId,string Provider,DateTimeOffset At,int ReservedBytes,string Digest);
public sealed record PilotResult(string ScopeId,string PlanId,string RunId,string Status,string EvidenceClass,string? CaptureDigest,string? EtlDigest,int Facts,string? Error,string Digest);
public sealed record PilotExecution(string ScopeId,string RunId,string Mode,string EvidenceClass,string Status,string[] Blockers,PilotResult[] Results,string Digest);

// Explicit one-shot orchestrator. Reuses transport, archive, quality, scheduler and ETL; never a HostedService.
public sealed class RealAcquisitionWorker(string root,Func<PilotTrust> trust,TimeProvider? clock=null,HttpMessageHandler? fixtureTransport=null,Func<PilotExecutionEvidence?>? executionEvidence=null)
{
    private readonly TimeProvider time=clock??TimeProvider.System;
    private DateTimeOffset Now=>time.GetUtcNow();
    private void RequireExecution(PilotScope scope,string run,PilotTrust current)
    {if(!scope.Requests[0].Fixture||executionEvidence is not null)PilotExecutionRules.Require(scope,run,root,current,executionEvidence?.Invoke(),Now);}
    private string Ledger=>Path.Combine(root,"pilot-acquisition");
    private static void Atomic<T>(string path,T value)
    {
        var temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        using(var file=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None)){System.Text.Json.JsonSerializer.Serialize(file,value);file.Flush(true);}
        File.Move(temp,path,false);
    }
    private static T Read<T>(string path)=>HistoricalDatasetStore.Read<T>(path);
    private void Storage(PilotScope scope,long reserve)
    {
        // Count all files in the isolated store, including pending and failed work. Sharing the store is conservative.
        var used=Directory.Exists(root)?Directory.EnumerateFiles(root,"*",SearchOption.AllDirectories).Sum(p=>new FileInfo(p).Length):0;
        if(used+reserve>scope.StorageLimitBytes)throw new InvalidOperationException("PILOT_STORAGE_LIMIT");
    }
    private static PilotResult Seal(PilotResult r)=>r with{Digest=ResearchMath.Digest(r with{Digest=""})};
    public async Task<PilotExecution> Run(PilotScope scope,PilotAuthorization authorization,string runId,string mode,CancellationToken token=default)
    {
        PilotScopeRules.Validate(scope);
        if(!PilotScopeRules.Digest(runId)||mode is not ("PLAN" or "DRY_RUN" or "ACQUIRE_ONCE" or "REPLAY" or "VALIDATE"))throw new ArgumentException("PILOT_MODE_OR_RUN_ID_INVALID");
        var fixture=scope.Requests[0].Fixture;
        if(!fixture&&fixtureTransport is not null)throw new InvalidOperationException("INJECTED_TRANSPORT_IS_FIXTURE_ONLY");
        if(fixture&&mode=="ACQUIRE_ONCE"&&fixtureTransport is null)throw new InvalidOperationException("FIXTURE_CANNOT_ACCESS_NETWORK");
        var evidence=fixture?"Synthetic Fixture":"Real Acquisition";
        PilotExecution Finish(string status,string[] blockers,PilotResult[] results){var r=new PilotExecution(scope.Id,runId,mode,evidence,status,blockers,results,"");return r with{Digest=ResearchMath.Digest(r)};}
        var currentTrust=trust();
        var initial=PilotScopeRules.Blockers(scope,authorization,currentTrust,Now,"ACQUIRE","STORE")
            .Concat(fixture&&executionEvidence is null?[]:PilotExecutionRules.Blockers(scope,runId,root,currentTrust,executionEvidence?.Invoke(),Now)).ToArray();
        if(mode is "PLAN" or "DRY_RUN")return Finish(initial.Length==0?"READY_NO_IO":"BLOCKED",initial,[]);
        if(mode=="ACQUIRE_ONCE"&&initial.Length>0)return Finish("BLOCKED",initial,[]);
        // Read-only recovery checks do not create directories or locks if no prior work exists.
        if(mode is "REPLAY" or "VALIDATE"&&!Directory.Exists(Ledger))return Finish("NO_CAPTURE",[],[]);
        token.ThrowIfCancellationRequested();Directory.CreateDirectory(Ledger);using var lease=AcquisitionSafety.Lease(Ledger);
        if(mode=="ACQUIRE_ONCE"){
            RequireExecution(scope,runId,trust());
            if(executionEvidence is not null){
                var executionProof=executionEvidence();PilotExecutionRules.Require(scope,runId,root,trust(),executionProof,Now);
                var evidencePath=Path.Combine(Ledger,ResearchMath.Digest(new{scope.Id,Run=runId})+".execution.json");
                if(File.Exists(evidencePath)){if(ResearchMath.Digest(Read<PilotExecutionEvidence>(evidencePath))!=ResearchMath.Digest(executionProof))throw new InvalidDataException("PILOT_EXECUTION_EVIDENCE_CHANGED");}
                else Atomic(evidencePath,executionProof);
            }
        }
        var scopePath=Path.Combine(Ledger,scope.Id+".scope.json");
        if(File.Exists(scopePath)){if(ResearchMath.Digest(Read<PilotScope>(scopePath))!=ResearchMath.Digest(scope))throw new InvalidDataException("PILOT_SCOPE_CHANGED");}
        else if(mode=="ACQUIRE_ONCE")Atomic(scopePath,scope);
        else return Finish("NO_CAPTURE",[],[]);
        var store=new ForwardCaptureStore(root);var output=new List<PilotResult>();
        using var client=fixtureTransport is null?AcquisitionSafety.Client():new HttpClient(fixtureTransport,false){Timeout=TimeSpan.FromSeconds(AcquisitionSafety.TimeoutSeconds)};
        foreach(var plan in scope.Requests)
        {
            token.ThrowIfCancellationRequested();
            var key=ResearchMath.Digest(new{Scope=scope.Id,Run=runId,Plan=plan.Id});var path=Path.Combine(Ledger,key);
            if(File.Exists(path+".result.json"))
            {
                var result=Read<PilotResult>(path+".result.json");VerifyResult(result,scope,plan,runId,key,store);output.Add(result);continue;
            }
            if(mode is "REPLAY" or "VALIDATE")
            {
                // A crash after raw publication is visible as incomplete, never a fabricated success.
                if(File.Exists(path+".raw.json"))ValidateRaw(Read<SourceArchive>(path+".raw.json"),plan,scope);
                var history=store.Replay(plan);history.Runs.Where(x=>x.IdempotencyKey==key).ToList().ForEach(x=>store.Raw(plan,key));
                output.Add(Seal(new(scope.Id,plan.Id,runId,"INCOMPLETE",evidence,null,null,0,"RESUME_ACQUIRE_ONCE", "")));continue;
            }
            var current=trust();PilotScopeRules.Require(scope,authorization,current,Now,"ACQUIRE","STORE");RequireExecution(scope,runId,current);AcquisitionSafety.RequireCurrentSources(root);
            try
            {
                SourceArchive raw;
                if(File.Exists(path+".raw.json")){raw=Read<SourceArchive>(path+".raw.json");ValidateRaw(raw,plan,scope);}
                else
                {
                    raw=await Acquire(scope,plan,authorization,runId,key,client,token);
                    token.ThrowIfCancellationRequested();current=trust();PilotScopeRules.Require(scope,authorization,current,Now,"ACQUIRE","STORE");RequireExecution(scope,runId,current);
                    Storage(scope,System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(raw).LongLength*4+65536);
                    Atomic(path+".raw.json",raw); // Recovery reuses this exact observation rather than re-downloading.
                }
                current=trust();PilotScopeRules.Require(scope,authorization,current,Now,"ACQUIRE","STORE");RequireExecution(scope,runId,current);
                var permissions=PilotScopeRules.Effective(scope,authorization,current);
                Storage(scope,System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(raw).LongLength*3+65536);
                var capture=store.Append(plan,new(plan.Id,key,raw,raw.RetrievedAt,new(null,null,"UNKNOWN")),permissions,permissions.Select(x=>x.Digest).ToHashSet(),Now);
                CaptureQualityReceipt quality;
                try{quality=store.Assess(plan,key,new(null,null,capture.DecisionAt,[]));}
                catch(Exception e) when(e is System.Text.Json.JsonException or InvalidDataException or ArgumentException or FormatException)
                {
                    var quarantined=Seal(new(scope.Id,plan.Id,runId,"QUARANTINED",evidence,capture.Digest,null,0,"SCHEMA_DRIFT", ""));
                    Atomic(path+".result.json",quarantined);output.Add(quarantined);continue;
                }
                EtlJobReport? etl=null;
                if(quality.Quality.Status!="QUARANTINED")
                {
                    // The operational dispatch date is NOT historical calendar evidence or a production scheduler approval.
                    var calendar=new TradingSessionCalendar("pilot-dispatch-only-v1",[plan.RequestDate],"");calendar=calendar with{Digest=ResearchMath.Digest(calendar)};
                    var schedulerRoot=Path.Combine(root,"pilot-scheduler");var scheduler=new EtlScheduler(schedulerRoot,time);
                    var request=ForwardCaptureStore.Request(plan,raw);
                    var normalized=new OfficialDataProviderAdapter(plan.Provider).Transform(request,raw);
                    Storage(scope,System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(normalized).LongLength*8+System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(raw).LongLength*6+262144);
                    var definition=new ScheduledJobDefinition("",request,EtlPipeline.Identity(request,raw.SourceUri,raw.Synthetic),raw.RetrievedAt,plan.RequestDate,calendar,true,3);
                    definition=definition with{Id=EtlScheduler.Identity(definition)};scheduler.Register(definition,raw);
                    var execution=await scheduler.RunOnce(definition.Id,token);
                    var latest=execution??scheduler.Snapshot().Single(x=>x.Definition.Id==definition.Id).Execution;
                    if(latest.Status is not ("SUCCEEDED" or "QUARANTINED"))throw new InvalidOperationException("PILOT_DISPATCH_PENDING");
                    etl=new EtlPipeline(Path.Combine(schedulerRoot,"etl")).Replay(definition.EtlJobId);
                }
                var result=Seal(new(scope.Id,plan.Id,runId,etl?.Status??"QUARANTINED",evidence,capture.Digest,etl?.Digest,etl?.LoadedFacts??0,null,""));
                Atomic(path+".result.json",result);output.Add(result);
            }
            catch(Exception e) when(e is IOException or InvalidDataException or HttpRequestException or InvalidOperationException or ArgumentException or System.Text.Json.JsonException or FormatException)
            {
                // Retriable/incomplete attempts have append-only failure events, not a final result that prevents recovery.
                var result=Seal(new(scope.Id,plan.Id,runId,"FAILED",evidence,null,null,0,ProviderErrorClassification.Classify(e),""));
                Atomic(path+"."+Guid.NewGuid().ToString("N")+".failure.json",result);output.Add(result);
            }
        }
        return Finish(output.All(x=>x.Status is "SUCCEEDED" or "QUARANTINED")?"COMPLETED_REVIEW_REQUIRED":"PARTIAL_OR_INCOMPLETE",[],output.ToArray());
    }
    private static void ValidateRaw(SourceArchive raw,CapturePlan p,PilotScope s)
    {
        new OfficialDataProviderAdapter(p.Provider).Validate(ForwardCaptureStore.Request(p,raw),raw);
        var bytes=Convert.FromBase64String(raw.BodyBase64);
        if(raw.Synthetic!=p.Fixture||bytes.Length>s.MaximumResponseBytes||Convert.ToHexString(SHA256.HashData(bytes))!=raw.SourceChecksum||
            raw.SourceUri!=p.SourceUrl||raw.ObservedAt<s.ForwardStart||raw.RetrievedAt>=s.ForwardEnd)throw new InvalidDataException("PILOT_RAW_INVALID");
    }
    private void VerifyResult(PilotResult r,PilotScope s,CapturePlan p,string runId,string key,ForwardCaptureStore store)
    {
        if(r.Digest!=Seal(r).Digest||r.ScopeId!=s.Id||r.PlanId!=p.Id||r.RunId!=runId||r.EvidenceClass!=(p.Fixture?"Synthetic Fixture":"Real Acquisition"))throw new InvalidDataException("PILOT_RESULT_CHANGED");
        var capture=store.Replay(p).Runs.Single(x=>x.IdempotencyKey==key);ValidateRaw(store.Raw(p,key),p,s);
        if(capture.Digest!=r.CaptureDigest)throw new InvalidDataException("PILOT_CAPTURE_CHANGED");
        if(r.Error=="SCHEMA_DRIFT"&&r.Status=="QUARANTINED"&&r.EtlDigest is null&&r.Facts==0)
        {
            try{var raw=store.Raw(p,key);new OfficialDataProviderAdapter(p.Provider).Transform(ForwardCaptureStore.Request(p,raw),raw);}
            catch(Exception e) when(e is System.Text.Json.JsonException or InvalidDataException or ArgumentException or FormatException){return;}
            throw new InvalidDataException("QUARANTINE_REASON_CHANGED");
        }
        var quality=CaptureQuality.Evaluate(ForwardCaptureStore.Request(p,store.Raw(p,key)),new OfficialDataProviderAdapter(p.Provider).Transform(ForwardCaptureStore.Request(p,store.Raw(p,key)),store.Raw(p,key)),new(null,null,capture.DecisionAt,[]));
        if(r.EtlDigest is not null){var raw=store.Raw(p,key);var etl=new EtlPipeline(Path.Combine(root,"pilot-scheduler","etl")).Replay(EtlPipeline.Identity(ForwardCaptureStore.Request(p,raw),raw.SourceUri,p.Fixture));if(etl.Digest!=r.EtlDigest||etl.LoadedFacts!=r.Facts||etl.Status!=r.Status)throw new InvalidDataException("PILOT_ETL_CHANGED");}
        else if(r.Status!="QUARANTINED"||quality.Status!="QUARANTINED"||r.Facts!=0)throw new InvalidDataException("PILOT_QUALITY_CHANGED");
    }
    private async Task<SourceArchive> Acquire(PilotScope s,CapturePlan p,PilotAuthorization a,string run,string key,HttpClient client,CancellationToken token)
    {
        for(var retry=0;retry<AcquisitionSafety.MaximumAttempts;retry++)
        {
            token.ThrowIfCancellationRequested();var current=trust();PilotScopeRules.Require(s,a,current,Now,"ACQUIRE","STORE");RequireExecution(s,run,current);PilotScopeRules.ValidateEndpoint(p);
            var tickets=Directory.GetFiles(Ledger,"*.ticket.json").Select(Read<PilotTicket>).ToArray();
            if(tickets.Any(t=>t.Digest!=ResearchMath.Digest(t with{Digest=""})))throw new InvalidDataException("PILOT_QUOTA_LEDGER_CHANGED");
            if(tickets.Count(t=>t.ScopeId==s.Id)>=s.MaxRequests||tickets.Count(t=>t.Provider==p.Provider&&t.At>Now.AddHours(-1))>=AcquisitionSafety.ProviderHourlyLimit)throw new InvalidOperationException("PILOT_QUOTA_EXHAUSTED");
            if(tickets.Count(t=>t.ScopeId==s.Id&&t.PlanId==p.Id&&t.RunId==run)>=AcquisitionSafety.MaximumAttempts)throw new InvalidOperationException("PILOT_RETRY_EXHAUSTED");
            if(tickets.Where(t=>t.ScopeId==s.Id).Sum(t=>(long)t.ReservedBytes)+s.MaximumResponseBytes>s.StorageLimitBytes)throw new InvalidOperationException("PILOT_STORAGE_LIMIT");
            var recent=Directory.GetFiles(Ledger,"*.network.json").Select(Read<NetworkAttempt>).Where(x=>x.Provider==p.Provider).OrderByDescending(x=>x.At).Take(3).ToArray();
            if(recent.Length==3&&recent.All(x=>!x.Success)&&recent[0].At>Now.AddMinutes(-5))throw new InvalidOperationException("PILOT_CIRCUIT_OPEN");
            var ticket=new PilotTicket(s.Id,p.Id,run,p.Provider,Now,s.MaximumResponseBytes,"");ticket=ticket with{Digest=ResearchMath.Digest(ticket)};
            Storage(s,s.MaximumResponseBytes*4L+65536);
            var id=Guid.NewGuid().ToString("N");Atomic(Path.Combine(Ledger,id+".ticket.json"),ticket);
            using var deadline=CancellationTokenSource.CreateLinkedTokenSource(token);deadline.CancelAfter(TimeSpan.FromSeconds(AcquisitionSafety.TimeoutSeconds));
            try
            {
                using var response=await client.GetAsync(p.SourceUrl,HttpCompletionOption.ResponseHeadersRead,deadline.Token);
                if((int)response.StatusCode is >=300 and <400)throw new InvalidDataException("PILOT_REDIRECT_FORBIDDEN");
                if(response.StatusCode==HttpStatusCode.TooManyRequests)throw new InvalidDataException("PILOT_PROVIDER_QUOTA");
                response.EnsureSuccessStatusCode();
                if(response.Content.Headers.ContentLength>s.MaximumResponseBytes)throw new InvalidDataException("PILOT_RESPONSE_TOO_LARGE");
                await using var stream=await response.Content.ReadAsStreamAsync(deadline.Token);using var buffer=new MemoryStream();var block=new byte[8192];
                int count;while((count=await stream.ReadAsync(block,deadline.Token))>0){if(buffer.Length+count>s.MaximumResponseBytes)throw new InvalidDataException("PILOT_RESPONSE_TOO_LARGE");buffer.Write(block,0,count);}
                var bytes=buffer.ToArray();var raw=new SourceArchive(key,p.Provider,Enum.Parse<HistoricalDatasetType>(p.Dataset),p.SourceUrl,ticket.At,Now,
                    Convert.ToBase64String(bytes),Convert.ToHexString(SHA256.HashData(bytes)),p.Provider+"-capture-v1",p.Fixture);
                ValidateRaw(raw,p,s);Atomic(Path.Combine(Ledger,id+".network.json"),new NetworkAttempt(p.Provider,Now,true));return raw;
            }
            catch(Exception e) when(e is HttpRequestException or OperationCanceledException or InvalidDataException)
            {
                Atomic(Path.Combine(Ledger,id+".network.json"),new NetworkAttempt(p.Provider,Now,false));
                if(token.IsCancellationRequested)throw;
                if(e is InvalidDataException||e is HttpRequestException h&&h.StatusCode is {} status&&(int)status<500||retry==AcquisitionSafety.MaximumAttempts-1)throw;
                await Task.Delay(TimeSpan.FromSeconds(retry+1),token);
            }
        }
        throw new InvalidOperationException("PILOT_RETRY_EXHAUSTED");
    }
    public sealed record NetworkAttempt(string Provider,DateTimeOffset At,bool Success);
}
