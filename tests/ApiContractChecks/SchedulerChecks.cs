using WorkJournal.Application.Research;
using WorkJournal.Web.Models.HistoricalResearch;
using WorkJournal.Web.Services.HistoricalResearch;
using WorkJournal.Web.Services.Research;
public static class SchedulerChecks
{
    private sealed class Clock : TimeProvider { public DateTimeOffset Now=new(2026,10,1,0,0,0,TimeSpan.Zero);public override DateTimeOffset GetUtcNow()=>Now; }
    public static async Task Run(ProviderRequest request,SourceArchive raw,Action<bool,string> check)
    {
        var root=Path.Combine(Path.GetTempPath(),"worklog-scheduler-"+Guid.NewGuid().ToString("N"));var clock=new Clock();var scheduler=new EtlScheduler(root,clock);
        var calendar=new TradingSessionCalendar("FIXTURE-CALENDAR-v1",[request.Date],"");calendar=calendar with{Digest=ResearchMath.Digest(calendar)};
        ScheduledJobDefinition Definition(int minute=0,bool enabled=true){var d=new ScheduledJobDefinition("",request,EtlPipeline.Identity(request,raw.SourceUri,raw.Synthetic),clock.Now.AddMinutes(minute),request.Date,calendar,enabled,3);return d with{Id=EtlScheduler.Identity(d)};}
        void Reject(Action action,string name){try{action();}catch(Exception e) when(e is IOException or InvalidDataException or InvalidOperationException or ArgumentException){check(true,name);return;}throw new Exception("Expected rejection "+name);}
        var definition=Definition();scheduler.Register(definition,raw);
        check(scheduler.Register(definition,raw).Execution.Revision==1,"Schedule registration idempotent");
        var contenders=await Task.WhenAll(Enumerable.Range(0,8).Select(_=>Task.Run(()=>{try{return new EtlScheduler(root,clock).Claim(definition.Id);}catch(IOException){return null;}})));
        var claim=contenders.Single(x=>x?.Status=="RUNNING")!;check(contenders.Count(x=>x?.Status=="RUNNING")==1,"Multiple workers acquire only one durable lease");
        check(scheduler.Heartbeat(definition.Id,claim.LeaseId!),"Lease heartbeat persisted");
        clock.Now=clock.Now.AddMinutes(3);var replacement=scheduler.Claim(definition.Id)!;
        check(replacement.Attempts==2&&replacement.LeaseId!=claim.LeaseId,"Expired lease recovered with a new fencing identity");
        Reject(()=>scheduler.Heartbeat(definition.Id,claim.LeaseId!),"Old lease cannot heartbeat after takeover");
        Reject(()=>scheduler.Finish(definition.Id,claim.LeaseId!,null,"PROVIDER_TIMEOUT"),"Stale worker cannot finalize");
        var retry=scheduler.Finish(definition.Id,replacement.LeaseId!,null,"PROVIDER_TIMEOUT");
        check(retry.Status=="RETRY"&&scheduler.Claim(definition.Id)==null,"Provider timeout uses persisted backoff");
        clock.Now=clock.Now.AddMinutes(1);var last=scheduler.Claim(definition.Id)!;
        check(scheduler.Finish(definition.Id,last.LeaseId!,null,"PROVIDER_QUOTA").Status=="FAILED","Quota failure respects bounded retry budget");
        var disabled=Definition(1,false);scheduler.Register(disabled,raw);clock.Now=clock.Now.AddMinutes(2);
        check(scheduler.Claim(disabled.Id)==null,"Disabled schedules never run");
        var cancelled=Definition();scheduler.Register(cancelled,raw);scheduler.Cancel(cancelled.Id);
        check(scheduler.Claim(cancelled.Id)==null,"Pending cancellation prevents dispatch");
        var invalid=Definition() with{Calendar=calendar with{Sessions=[]}};invalid=invalid with{Id=EtlScheduler.Identity(invalid)};
        Reject(()=>scheduler.Register(invalid,raw),"Missing or altered trading calendar fails closed");
        // Simulate a crash after immutable batch commit and before completion receipt publication.
        var recoveryRoot=Path.Combine(root,"recovery");var recover=new EtlScheduler(recoveryRoot,clock);var d=Definition();recover.Register(d,raw);var lease=recover.Claim(d.Id)!;
        var pipeline=new EtlPipeline(Path.Combine(recoveryRoot,"etl"));var completed=pipeline.Execute(request,raw);
        var resultPath=Path.Combine(recoveryRoot,"etl",completed.JobId,"01.result.json");File.Move(resultPath,resultPath+".crash-fixture");
        recover.Cancel(d.Id); // Cancellation cannot pretend an already committed batch was rolled back.
        clock.Now=clock.Now.AddMinutes(3);var recovered=recover.Claim(d.Id)!;
        check(recovered.Status=="SUCCEEDED"&&recovered.Attempts==1&&pipeline.Replay(completed.JobId).ErrorCode=="RECOVERED_VERIFIED_COMMIT","Crash recovery verifies original batch without a new dispatch");
        check(Directory.GetFiles(Path.Combine(recoveryRoot,"etl",completed.JobId),"batch.json").Length==1&&recover.Claim(d.Id)==null,"Recovery commits exactly one batch");
        var completedView=recover.Snapshot().Single();
        var receiptPath=Path.Combine(recoveryRoot,"schedules",d.Id,$"{completedView.Execution.Revision:D12}.receipt.json");
        File.WriteAllText(receiptPath,"{}");Reject(()=>recover.Snapshot(),"Scheduler receipt tampering fails closed");
        var runRoot=Path.Combine(root,"run");var run=new EtlScheduler(runRoot,clock);var runDef=Definition();run.Register(runDef,raw);
        var executed=await run.RunOnce(runDef.Id);check(executed?.Status=="SUCCEEDED","Controlled worker executes the existing ETL pipeline");
        var snapshot=Phase4LOperations.Snapshot(run.Snapshot(),clock.Now);Phase4LOperations.Validate(snapshot);
        check(!snapshot.ProductionSchedulerEnabled&&snapshot.Semantics.Contains("NOT_LIVE"),"Operations snapshot does not claim production scheduler or live health");
        var quota=new EtlScheduler(Path.Combine(root,"quota"),clock);
        for(var i=0;i<20;i++){var q=Definition(-i-1);quota.Register(q,raw);var c=quota.Claim(q.Id)!;quota.Finish(q.Id,c.LeaseId!,null,"EXECUTION_FAILED");}
        var limited=Definition(-21);quota.Register(limited,raw);
        check(quota.Claim(limited.Id)==null,"Twenty persisted provider dispatches enforce hourly quota");
        clock.Now=clock.Now.AddHours(1).AddSeconds(1);
        check(quota.Claim(limited.Id)?.Status=="RUNNING","Provider quota window expires in UTC");
        var circuit=new EtlScheduler(Path.Combine(root,"circuit"),clock);
        for(var i=0;i<3;i++){var cdef=Definition(-i-1);circuit.Register(cdef,raw);var c=circuit.Claim(cdef.Id)!;circuit.Finish(cdef.Id,c.LeaseId!,null,"PROVIDER_TIMEOUT");}
        var probe=Definition(-4);circuit.Register(probe,raw);
        check(circuit.Claim(probe.Id)==null,"Three transient provider failures open the circuit");
        clock.Now=clock.Now.AddMinutes(6);var probeLease=circuit.Claim(probe.Id);
        var other=Definition(-5);circuit.Register(other,raw);
        check(probeLease?.Status=="RUNNING"&&circuit.Claim(other.Id)==null,"Circuit cooldown permits only one half-open probe");
        circuit.Cancel(probe.Id);
        check(!circuit.Heartbeat(probe.Id,probeLease!.LeaseId!),"Running cancellation reaches worker heartbeat");
        check(circuit.Finish(probe.Id,probeLease.LeaseId!,null,"PROVIDER_TIMEOUT").Status=="CANCELLED","Running cancellation becomes a durable terminal receipt");
        var corruptRoot=Path.Combine(root,"corrupt");var corrupt=new EtlScheduler(corruptRoot,clock);var corruptDef=Definition();corrupt.Register(corruptDef,raw);corrupt.Claim(corruptDef.Id);
        var committed=new EtlPipeline(Path.Combine(corruptRoot,"etl")).Execute(request,raw);
        File.WriteAllText(Path.Combine(corruptRoot,"etl",committed.JobId,"batch.json"),"{}");clock.Now=clock.Now.AddMinutes(3);
        check(corrupt.Claim(corruptDef.Id)?.Reason=="RECOVERY_INTEGRITY_BLOCKED","Expired lease never accepts a corrupted original commit");
        var thirdRoot=Path.Combine(root,"third-attempt");var third=new EtlPipeline(thirdRoot);
        var badRaw=raw with{BodyBase64=Convert.ToBase64String("tampered"u8.ToArray())};
        third.Execute(request,badRaw);third.Execute(request,badRaw,true);var finalCommit=third.Execute(request,raw,true);
        var finalPath=Path.Combine(thirdRoot,finalCommit.JobId,"03.result.json");File.Move(finalPath,finalPath+".crash-fixture");
        check(third.Execute(request,raw,true).Status=="SUCCEEDED"&&third.Replay(finalCommit.JobId).Attempt==3,"Committed third attempt is recovered before retry ceiling");
    }
}
