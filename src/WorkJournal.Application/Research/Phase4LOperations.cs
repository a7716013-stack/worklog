using WorkJournal.Web.Services.Research;
namespace WorkJournal.Application.Research;

public sealed record EvidenceClosureWork(string TaskId,string ClaimKey,int Priority,string EvidenceRequirement,string NextAction,
    string TaskOwner,string ReviewStatus,bool ManualActionRequired,string[] ReviewReceipts,string? ClosureReceipt,SourceImpact[] RevocationImpacts);
public sealed record EtlScheduleStatus(string Id,string Provider,bool Enabled,DateTimeOffset DueAt,string CalendarVersion,string CalendarDigest,
    string Status,int Attempts,DateTimeOffset? HeartbeatAt,DateTimeOffset? LeaseExpiresAt,DateTimeOffset NextAttemptAt,bool CancellationRequested,
    string? EtlReceiptDigest,string? Reason,string ReceiptDigest);
public sealed record EtlOperationsSnapshot(string SchemaVersion,DateTimeOffset ExportedAt,string Semantics,bool ProductionSchedulerEnabled,
    EtlScheduleStatus[] Schedules,string Digest);
public sealed record EtlProviderStatus(string Provider,string Status,DateTimeOffset VerifiedAt,int SuccessfulJobs,int FailedJobs,int QuarantinedJobs);
public sealed record EtlAlert(string Code,string Severity,string SubjectId,string NextAction);
public sealed record EtlOperationsReport(string SchemaVersion,DateTimeOffset ExportedAt,string Semantics,bool ProductionSchedulerEnabled,
    EtlScheduleStatus[] Schedules,EtlProviderStatus[] Providers,EvidenceClosureWork[] Closure,SourceImpact[] Invalidations,DatasetBuildReport[] Readiness,
    EtlAlert[] Alerts,string EtlDigest,string EvidenceDigest,string SchedulerDigest,string Digest);
public static class Phase4LOperations
{
    public static EtlOperationsSnapshot Snapshot(ScheduledJobView[] jobs,DateTimeOffset at)
    {
        var r=new EtlOperationsSnapshot("EtlOperations-v1",at,"CONTROLLED_EXPORT_SNAPSHOT_NOT_LIVE_HEALTH",false,
            jobs.Select(j=>new EtlScheduleStatus(j.Definition.Id,j.Definition.Request.Provider,j.Definition.Enabled,j.Definition.DueAt,j.Definition.Calendar.Version,j.Definition.Calendar.Digest,
                j.Execution.Status,j.Execution.Attempts,j.Execution.HeartbeatAt,j.Execution.LeaseExpiresAt,j.Execution.NextAttemptAt,j.Execution.CancellationRequested,
                j.Execution.EtlReceiptDigest,j.Execution.Reason,j.Execution.Digest)).ToArray(),"");
        return r with{Digest=ResearchMath.Digest(r)};
    }
    public static void Validate(EtlOperationsSnapshot r)
    {
        if(r.SchemaVersion!="EtlOperations-v1"||r.Semantics!="CONTROLLED_EXPORT_SNAPSHOT_NOT_LIVE_HEALTH"||r.ProductionSchedulerEnabled||
            r.ExportedAt.Offset!=TimeSpan.Zero||r.Digest!=ResearchMath.Digest(r with{Digest=""})||r.Schedules.Select(s=>s.Id).Distinct().Count()!=r.Schedules.Length)
            throw new InvalidDataException("OPERATIONS_SNAPSHOT_INVALID");
    }
    public static EvidenceClosureWork[] Closure(Phase4JReport evidence,Phase4IReport parent)
    {
        Phase4JProjection.Validate(evidence,parent);
        return evidence.Plan.Select(task=>{
            var requirement=Phase4IProjection.Queue(parent).Single(c=>c.Id==task.ClaimKey);
            var receipts=requirement.Decisions.Select(d=>d.ReviewDigest).Distinct().Order(StringComparer.Ordinal).ToArray();
            var impacts=evidence.Impacts.Where(i=>i.SourceId==task.SourceId&&i.SourceVersion==task.SourceVersion).ToArray();
            var closed=requirement.Applicability=="SUPPORTED_FOR_REVIEWED_SCOPE"&&impacts.Length==0&&requirement.Claim.ResolutionReceiptDigest is not null;
            // Roles only, not fabricated assignments to a person. Existing reviewer CLI remains authoritative.
            return new EvidenceClosureWork(task.TaskId,task.ClaimKey,task.Priority,requirement.RequiredProof+"; "+task.ExpectedEvidence,task.NextAction,
                task.ExecutionMode=="MANUAL_ACTION_REQUIRED"?"LICENSE_OPERATOR_UNASSIGNED":"EVIDENCE_REVIEWER_UNASSIGNED",
                impacts.Length>0?"REVIEW_REQUIRED_SOURCE_INVALIDATED":closed?"SUPPORTED_FOR_REVIEWED_SCOPE":receipts.Length>0?"REVIEWED_UNRESOLVED":"REVIEW_REQUIRED",
                !closed,receipts,closed?requirement.Claim.ResolutionReceiptDigest:null,impacts);
        }).OrderBy(t=>t.Priority).ThenBy(t=>t.TaskId,StringComparer.Ordinal).ToArray();
    }
    public static EtlOperationsReport Create(EtlPublication etl,EtlOperationsSnapshot scheduler,Phase4JReport evidence,Phase4IReport parent)
    {
        EtlPipeline.ValidatePublication(etl);Validate(scheduler);var closure=Closure(evidence,parent);
        var providers=etl.Providers.Select(p=>new EtlProviderStatus(p.Provider,"HISTORICAL_EXPORT_ONLY",etl.VerifiedAt,
            etl.Jobs.Count(j=>j.Request.Provider==p.Provider&&j.Status=="SUCCEEDED"),etl.Jobs.Count(j=>j.Request.Provider==p.Provider&&j.Status=="FAILED"),etl.Jobs.Count(j=>j.Request.Provider==p.Provider&&j.Status=="QUARANTINED"))).ToArray();
        var alerts=new List<EtlAlert>();
        foreach(var j in etl.Jobs.Where(j=>j.Status is "FAILED" or "QUARANTINED"))alerts.Add(new("ETL_"+j.Status,"WARNING",j.JobId,"Inspect existing quality report and original lineage before retry"));
        foreach(var j in scheduler.Schedules.Where(s=>s.Status is "FAILED" or "QUARANTINED" or "RETRY"))alerts.Add(new("SCHEDULE_"+j.Status,"WARNING",j.Id,"Review execution receipt and original batch before controlled retry"));
        foreach(var j in scheduler.Schedules.Where(s=>s.Status=="RUNNING"&&s.LeaseExpiresAt<=scheduler.ExportedAt))alerts.Add(new("LEASE_EXPIRED_AT_EXPORT","WARNING",j.Id,"Inspect original commit and digests before recovery"));
        foreach(var i in evidence.Impacts)alerts.Add(new("SOURCE_INVALIDATED","ERROR",i.SourceId,"Reevaluate existing authority and independent component approval"));
        if(closure.Any(c=>c.ClosureReceipt is null))alerts.Add(new("HISTORICAL_AUTHORITY_UNRESOLVED","WARNING","authority","Supply original publication and revision evidence through existing reviewer workflow"));
        if(parent.Counts.ApprovedResearchSamples==0)alerts.Add(new("RESEARCH_NOT_READY","INFO","Trend","Retain NOT_RUN; complete PIT, independent approval and TrendSample-v1 assessment"));
        var r=new EtlOperationsReport("Phase4L-v1",scheduler.ExportedAt,scheduler.Semantics,false,scheduler.Schedules,providers,closure,evidence.Impacts,evidence.Builds,alerts.ToArray(),etl.Digest,evidence.Digest,scheduler.Digest,"");
        return r with{Digest=ResearchMath.Digest(r)};
    }
}
