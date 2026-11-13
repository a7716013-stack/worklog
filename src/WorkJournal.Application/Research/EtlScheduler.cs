using WorkJournal.Web.Models.HistoricalResearch;
using WorkJournal.Web.Services.HistoricalResearch;
using WorkJournal.Web.Services.Research;
namespace WorkJournal.Application.Research;

public sealed record TradingSessionCalendar(string Version, DateOnly[] Sessions, string Digest);
public sealed record ScheduledJobDefinition(string Id, ProviderRequest Request, string EtlJobId,
    DateTimeOffset DueAt, DateOnly SessionDate, TradingSessionCalendar Calendar, bool Enabled, int MaximumAttempts);
public sealed record ScheduledExecution(string ScheduleId, long Revision, string Status, int Attempts, string? LeaseId,
    DateTimeOffset? HeartbeatAt, DateTimeOffset? LeaseExpiresAt, DateTimeOffset NextAttemptAt, bool CancellationRequested,
    string? EtlReceiptDigest, string? Reason, DateTimeOffset At, string PreviousDigest, string Digest);
public sealed record ScheduledJobView(ScheduledJobDefinition Definition, ScheduledExecution Execution);
public sealed record ProviderDispatchReceipt(string Provider, DateTimeOffset At, string ScheduleId, long Revision);

// Explicit controlled archive dispatch, never registered as a production HostedService.
// All workers MUST use the same durable volume; exclusive filesystem locks are required.
public sealed class EtlScheduler(string root, TimeProvider? clock = null)
{
    private readonly TimeProvider time = clock ?? TimeProvider.System;
    private DateTimeOffset Now => time.GetUtcNow();
    public static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(2);
    private string Jobs => Path.Combine(root,"schedules");
    private string Folder(string id) { if(id.Length!=64 || !id.All(Uri.IsHexDigit))throw new ArgumentException("SCHEDULE_ID_INVALID");return Path.Combine(Jobs,id); }
    private FileStream Gate()
    {
        Directory.CreateDirectory(root);
        for(var attempt=0;;attempt++)
        {
            try{return AcquisitionSafety.Lease(root);}
            catch(IOException) when(attempt<50){Thread.Sleep(20);}
        }
    }
    public static string Identity(ScheduledJobDefinition d)=>ResearchMath.Digest(d with{Id=""});
    private static void Validate(ScheduledJobDefinition d)
    {
        if(d.Id!=Identity(d)||d.DueAt.Offset!=TimeSpan.Zero||d.MaximumAttempts is <1 or >3||
            !System.Text.RegularExpressions.Regex.IsMatch(d.Calendar.Version,"^[A-Za-z0-9][A-Za-z0-9._-]{0,79}$")||d.Calendar.Digest!=ResearchMath.Digest(d.Calendar with{Digest=""})||
            d.Calendar.Sessions.Distinct().Count()!=d.Calendar.Sessions.Length||!d.Calendar.Sessions.Contains(d.SessionDate)||
            d.SessionDate<d.Request.CoverageStart||d.SessionDate>d.Request.CoverageEnd)
            throw new InvalidDataException("SCHEDULE_OR_CALENDAR_INVALID");
    }
    public ScheduledJobView Register(ScheduledJobDefinition definition, SourceArchive raw)
    {
        Validate(definition);
        if(definition.EtlJobId!=EtlPipeline.Identity(definition.Request,raw.SourceUri,raw.Synthetic))throw new InvalidDataException("SCHEDULE_SOURCE_BINDING");
        // Validate checksum/schema/URL before accepting a dispatch, never perform network acquisition here.
        new OfficialDataProviderAdapter(definition.Request.Provider).Transform(definition.Request,raw);
        using var gate=Gate();var folder=Folder(definition.Id);Directory.CreateDirectory(folder);
        var path=Path.Combine(folder,"definition.json");
        if(File.Exists(path)){var prior=Read(definition.Id);if(ResearchMath.Digest(prior.Definition)!=ResearchMath.Digest(definition))throw new InvalidDataException("SCHEDULE_CONFLICT");return prior;}
        var rawPath=Path.Combine(folder,"raw.json");
        if(!File.Exists(rawPath))HistoricalDatasetStore.WriteNew(rawPath,raw);
        else if(ResearchMath.Digest(HistoricalDatasetStore.Read<SourceArchive>(rawPath))!=ResearchMath.Digest(raw))throw new InvalidDataException("SCHEDULE_SOURCE_CONFLICT");
        HistoricalDatasetStore.WriteNew(path,definition);
        var state=new ScheduledExecution(definition.Id,1,definition.Enabled?"PENDING":"DISABLED",0,null,null,null,definition.DueAt,false,null,null,Now,"","");
        return new(definition,Append(folder,state));
    }
    private static ScheduledExecution Append(string folder,ScheduledExecution state)
    {
        state=state with{Digest=ResearchMath.Digest(state with{Digest=""})};
        // Atomic publication: a crash leaves only an ignored temporary file, never a partial receipt.
        var destination=Path.Combine(folder,$"{state.Revision:D12}.receipt.json");
        var temp=Path.Combine(folder,Guid.NewGuid().ToString("N")+".tmp");
        using(var stream=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None))
        {System.Text.Json.JsonSerializer.Serialize(stream,state);stream.Flush(true);}
        File.Move(temp,destination,false);return state;
    }
    private ScheduledExecution Save(ScheduledJobView job,ScheduledExecution state)=>Append(Folder(job.Definition.Id),state with{
        Revision=job.Execution.Revision+1,PreviousDigest=job.Execution.Digest,At=Now,Digest=""});
    private ScheduledJobView Read(string id)
    {
        var folder=Folder(id);var definition=HistoricalDatasetStore.Read<ScheduledJobDefinition>(Path.Combine(folder,"definition.json"));Validate(definition);
        if(definition.Id!=id)throw new InvalidDataException("SCHEDULE_ID_CHANGED");
        ScheduledExecution? previous=null;
        foreach(var path in Directory.GetFiles(folder,"*.receipt.json").Order(StringComparer.Ordinal))
        {
            var state=HistoricalDatasetStore.Read<ScheduledExecution>(path);
            if(state.ScheduleId!=id||state.Revision!=(previous?.Revision??0)+1||state.PreviousDigest!=(previous?.Digest??"")||
                state.Digest!=ResearchMath.Digest(state with{Digest=""})||state.At.Offset!=TimeSpan.Zero||state.At<previous?.At||
                state.Attempts is <0 or >3||state.Status is not ("DISABLED" or "PENDING" or "RUNNING" or "RETRY" or "FAILED" or "CANCELLED" or "SUCCEEDED" or "QUARANTINED"))throw new InvalidDataException("SCHEDULE_RECEIPT_CHANGED");
            previous=state;
        }
        return new(definition,previous??throw new InvalidDataException("SCHEDULE_INCOMPLETE"));
    }
    public ScheduledJobView[] Snapshot(){using var gate=Gate();return All();}
    private ScheduledJobView[] All()=>Directory.Exists(Jobs)?Directory.GetDirectories(Jobs).Where(p=>File.Exists(Path.Combine(p,"definition.json"))).Select(p=>Read(Path.GetFileName(p))).OrderBy(j=>j.Definition.Id,StringComparer.Ordinal).ToArray():[];
    public ScheduledExecution? Claim(string id)
    {
        using var gate=Gate();var job=Read(id);var s=job.Execution;var now=Now;
        if(!job.Definition.Enabled||s.Status is "SUCCEEDED" or "QUARANTINED" or "FAILED" or "CANCELLED"||s.NextAttemptAt>now||s.LeaseExpiresAt>now)return null;
        // Recovery inspects the original immutable ETL commit BEFORE retry limits or provider dispatch.
        var etlRoot=Path.Combine(root,"etl");var pipeline=new EtlPipeline(etlRoot);
        var etlFolder=Path.Combine(etlRoot,job.Definition.EtlJobId);
        if(Directory.Exists(etlFolder))
        {
            try
            {
                EtlJobReport? result=null;
                if(File.Exists(Path.Combine(etlFolder,"batch.json")))result=pipeline.Execute(job.Definition.Request,Raw(job));
                else if(Directory.GetFiles(etlFolder,"*.result.json").Length>0)result=pipeline.Replay(job.Definition.EtlJobId);
                if(result?.Status is "SUCCEEDED" or "QUARANTINED")return Save(job,s with{Status=result.Status,LeaseId=null,LeaseExpiresAt=null,EtlReceiptDigest=result.Digest,Reason="VERIFIED_ORIGINAL_RECEIPT"});
            }
            catch(IOException){return null;} // Original worker still holds the ETL lock; do not steal its commit.
            catch(Exception e) when(e is InvalidDataException or InvalidOperationException or ArgumentException or System.Text.Json.JsonException)
            {return Save(job,s with{Status="FAILED",LeaseId=null,LeaseExpiresAt=null,Reason="RECOVERY_INTEGRITY_BLOCKED"});}
        }
        if(s.CancellationRequested)return Save(job,s with{Status="CANCELLED",LeaseId=null,LeaseExpiresAt=null,Reason="OPERATOR_CANCELLED"});
        if(s.Attempts>=job.Definition.MaximumAttempts)return Save(job,s with{Status="FAILED",LeaseId=null,LeaseExpiresAt=null,Reason="RETRY_LIMIT"});
        var provider=job.Definition.Request.Provider;
        var dispatchDir=Path.Combine(root,"dispatch");Directory.CreateDirectory(dispatchDir);
        var dispatches=Directory.GetFiles(dispatchDir,"*.json").Select(HistoricalDatasetStore.Read<ProviderDispatchReceipt>).ToArray();
        if(dispatches.Count(d=>d.Provider==provider && d.At>now.AddHours(-1))>=20)return null;
        var peers=All().Where(j=>j.Definition.Request.Provider==provider).ToArray();
        // One provider dispatch at a time, including the single half-open probe after cooldown.
        if(peers.Any(j=>j.Execution.Status=="RUNNING"&&j.Execution.LeaseExpiresAt>now))return null;
        var recent=peers.SelectMany(j=>Directory.GetFiles(Folder(j.Definition.Id),"*.receipt.json").Select(HistoricalDatasetStore.Read<ScheduledExecution>))
            .Where(x=>x.Reason is "PROVIDER_TIMEOUT" or "PROVIDER_QUOTA" or "PROVIDER_TRANSIENT"||x.Status=="SUCCEEDED").OrderByDescending(x=>x.At).Take(3).ToArray();
        if(recent.Length==3&&recent.All(x=>x.Status!="SUCCEEDED")&&recent[0].At>now.AddMinutes(-5))return null;
        var claim=s with{Status="RUNNING",Attempts=s.Attempts+1,LeaseId=Guid.NewGuid().ToString("N"),HeartbeatAt=now,LeaseExpiresAt=now+LeaseDuration,Reason="LEASE_ACQUIRED"};
        // Conservative quota receipt first: a crash can consume quota, never exceed it.
        HistoricalDatasetStore.WriteNew(Path.Combine(dispatchDir,Guid.NewGuid().ToString("N")+".json"),new ProviderDispatchReceipt(provider,now,id,s.Revision+1));
        return Save(job,claim);
    }
    private SourceArchive Raw(ScheduledJobView job)=>HistoricalDatasetStore.Read<SourceArchive>(Path.Combine(Folder(job.Definition.Id),"raw.json"));
    private ScheduledJobView Owned(string id,string lease)
    {var job=Read(id);if(job.Execution.Status!="RUNNING"||job.Execution.LeaseId!=lease||job.Execution.LeaseExpiresAt<=Now)throw new InvalidOperationException("LEASE_LOST");return job;}
    public bool Heartbeat(string id,string lease)
    {using var gate=Gate();var job=Owned(id,lease);if(job.Execution.CancellationRequested)return false;Save(job,job.Execution with{HeartbeatAt=Now,LeaseExpiresAt=Now+LeaseDuration});return true;}
    public ScheduledExecution Cancel(string id)
    {using var gate=Gate();var job=Read(id);if(job.Execution.Status is "SUCCEEDED" or "QUARANTINED" or "FAILED" or "CANCELLED")return job.Execution;return Save(job,job.Execution with{CancellationRequested=true,Status=job.Execution.Status=="RUNNING"?"RUNNING":"CANCELLED",Reason="OPERATOR_CANCELLED"});}
    public ScheduledExecution Finish(string id,string lease,EtlJobReport? result,string? failure=null)
    {
        using var gate=Gate();var job=Owned(id,lease);var s=job.Execution;
        if(result is not null){EtlPipeline.Validate(result);if(result.JobId!=job.Definition.EtlJobId)throw new InvalidDataException("ETL_RECEIPT_BINDING");
            var replay=new EtlPipeline(Path.Combine(root,"etl")).Replay(result.JobId);if(replay.Digest!=result.Digest)throw new InvalidDataException("ETL_RECEIPT_CHANGED");}
        if(failure is not (null or "PROVIDER_TIMEOUT" or "PROVIDER_QUOTA" or "PROVIDER_TRANSIENT" or "EXECUTION_FAILED"))throw new ArgumentException("FAILURE_CODE_INVALID");
        var status=result?.Status;var retry=failure is "PROVIDER_TIMEOUT" or "PROVIDER_QUOTA" or "PROVIDER_TRANSIENT";
        if(status is not ("SUCCEEDED" or "QUARANTINED"))status=s.CancellationRequested||status=="CANCELLED"?"CANCELLED":retry&&s.Attempts<job.Definition.MaximumAttempts?"RETRY":"FAILED";
        return Save(job,s with{Status=status,LeaseId=null,LeaseExpiresAt=null,EtlReceiptDigest=result?.Digest,Reason=failure??result?.ErrorCode,
            NextAttemptAt=Now.AddSeconds(30*Math.Pow(2,s.Attempts-1))});
    }
    public async Task<ScheduledExecution?> RunOnce(string id,CancellationToken ct=default)
    {
        var claim=Claim(id);if(claim?.Status!="RUNNING")return claim;
        using var stop=CancellationTokenSource.CreateLinkedTokenSource(ct);
        var heartbeat=Task.Run(async()=>{try{while(true){await Task.Delay(TimeSpan.FromSeconds(20),time,stop.Token);if(!Heartbeat(id,claim.LeaseId!)){stop.Cancel();return;}}}catch(OperationCanceledException){}catch{stop.Cancel();}});
        EtlJobReport? result=null;string? failure=null;
        try{var job=Read(id);result=await Task.Run(()=>new EtlPipeline(Path.Combine(root,"etl")).Execute(job.Definition.Request,Raw(job),true,stop.Token),CancellationToken.None);}
        catch(Exception e) when(e is IOException or InvalidDataException or InvalidOperationException or ArgumentException or System.Text.Json.JsonException){failure="EXECUTION_FAILED";}
        finally{stop.Cancel();await heartbeat;}
        return Finish(id,claim.LeaseId!,result,failure);
    }
}
