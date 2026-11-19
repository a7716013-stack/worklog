using System.Security.Cryptography;
using System.Text.RegularExpressions;
using WorkJournal.Web.Models.HistoricalResearch;
using WorkJournal.Web.Services.HistoricalResearch;
using WorkJournal.Web.Services.Research;
namespace WorkJournal.Application.Research;

public sealed record CapturePlan(string Id,string Provider,string Dataset,string Instrument,DateOnly RequestDate,DateOnly CoverageStart,DateOnly CoverageEnd,
    string SourceUrl,string SchemaVersion,string SourceCodeSha,bool Fixture,int MinimumRetentionDays,string RetentionPolicy);
public sealed record CapturePermission(string PlanId,string Purpose,string Status,DateTimeOffset ValidFrom,DateTimeOffset ExpiresAt,string Reviewer,
    string EvidenceReference,int RetentionDays,string Digest);
public sealed record PublicationObservation(DateTimeOffset? ClaimedPublishedAt,string? OriginalEvidenceDigest,string Status);
public sealed record CaptureInput(string PlanId,string IdempotencyKey,SourceArchive Raw,DateTimeOffset DecisionAt,PublicationObservation Publication);
public sealed record CaptureRun(int Sequence,int VersionSequence,string PlanId,string IdempotencyKey,string InputDigest,string RawDigest,string ArchiveId,
    DateTimeOffset ObservedAt,DateTimeOffset RetrievedAt,DateTimeOffset DecisionAt,PublicationObservation Publication,bool Fixture,bool RevisionDetected,
    DateTimeOffset RetainUntil,string PreviousDigest,string Digest);
public sealed record CaptureHistory(string PlanId,CaptureRun[] Runs,string EvidenceClass,string Digest);
public sealed record CaptureQualityReceipt(string PlanId,string CaptureDigest,CaptureQualityContext Context,CaptureQualityReport Quality,string Digest);

// Complements SourceArchive/EtlPipeline. No HTTP acquisition or timer; protected local store only.
public sealed class ForwardCaptureStore(string root)
{
    public static string PlanId(CapturePlan p)=>ResearchMath.Digest(p with{Id=""});
    private static void Digest(string value){if(value.Length!=64||!value.All(Uri.IsHexDigit))throw new ArgumentException("INVALID_CAPTURE_DIGEST");}
    private string Folder(string id){Digest(id);return Path.Combine(root,"forward-capture",id);}
    public static void ValidatePlan(CapturePlan p)
    {
        if(p is null)throw new InvalidDataException("CAPTURE_PLAN_MISSING");
        if(p.Id!=PlanId(p)||p.SourceCodeSha.Length!=40||!p.SourceCodeSha.All(Uri.IsHexDigit)||p.MinimumRetentionDays is <1 or >3650||string.IsNullOrWhiteSpace(p.RetentionPolicy))throw new InvalidDataException("CAPTURE_PLAN_INVALID");
        var capability=OfficialDataProviderAdapter.Catalog().SingleOrDefault(c=>c.Provider==p.Provider);
        if(capability is null||!capability.Datasets.Contains(p.Dataset)||p.SchemaVersion!=capability.SchemaVersion||p.CoverageStart>p.CoverageEnd||
            p.RequestDate<p.CoverageStart||p.RequestDate>p.CoverageEnd||!Regex.IsMatch(p.Instrument,"^[0-9]{4}[0-9A-Z]{0,2}$"))throw new InvalidDataException("CAPTURE_CONTRACT_INVALID");
        AcquisitionSafety.ValidateUrl(p.SourceUrl);
    }
    public static string[] AuthorizationBlockers(CapturePlan p,CapturePermission[] receipts,IReadOnlySet<string> trusted,DateTimeOffset now,DateTimeOffset? observedAt=null)
    {
        ValidatePlan(p);
        if(p.Fixture)return [];
        return new[]{"ACQUIRE","STORE"}.Where(purpose=>!receipts.Any(x=>x.PlanId==p.Id&&x.Purpose==purpose&&x.Status=="GRANTED"&&
            x.ValidFrom.Offset==TimeSpan.Zero&&x.ExpiresAt.Offset==TimeSpan.Zero&&x.ValidFrom<=now&&x.ExpiresAt>now&&
            (purpose!="ACQUIRE"||x.ValidFrom<=(observedAt??now))&&
            !string.IsNullOrWhiteSpace(x.Reviewer)&&!string.IsNullOrWhiteSpace(x.EvidenceReference)&&x.RetentionDays>=p.MinimumRetentionDays&&
            x.Digest==ResearchMath.Digest(x with{Digest=""})&&trusted.Contains(x.Digest))).Select(x=>x+"_RIGHTS_AND_SCOPED_APPROVAL_REQUIRED").ToArray();
    }
    public static ProviderRequest Request(CapturePlan plan,SourceArchive raw)=>new(plan.Provider,plan.Dataset,plan.Instrument,plan.RequestDate,plan.CoverageStart,plan.CoverageEnd,raw.SourceChecksum,plan.SourceCodeSha,plan.SchemaVersion);
    private static void ValidateInput(CapturePlan p,CaptureInput input)
    {
        if(input is null||input.Raw is null||input.Publication is null||input.IdempotencyKey is null)throw new InvalidDataException("CAPTURE_INPUT_MISSING");
        ValidatePlan(p);Digest(input.IdempotencyKey);var a=input.Raw;
        if(input.PlanId!=p.Id||a.Synthetic!=p.Fixture||a.SourceUri!=p.SourceUrl||input.DecisionAt.Offset!=TimeSpan.Zero||
            a.ObservedAt.Offset!=TimeSpan.Zero||a.RetrievedAt.Offset!=TimeSpan.Zero||a.ObservedAt>a.RetrievedAt||string.IsNullOrWhiteSpace(a.ArchiveId))throw new InvalidDataException("CAPTURE_SCOPE_OR_TIME_INVALID");
        // Retain malformed source bodies for diagnosis; only known envelope and raw checksum are required for Bronze.
        new OfficialDataProviderAdapter(p.Provider).Validate(Request(p,a),a);
        if(a.BodyBase64.Length>AcquisitionSafety.MaximumBytes*4/3+8)throw new InvalidDataException("ARTIFACT_TOO_LARGE");
        var bytes=Convert.FromBase64String(a.BodyBase64);
        if(bytes.Length>AcquisitionSafety.MaximumBytes||Convert.ToHexString(SHA256.HashData(bytes))!=a.SourceChecksum)throw new InvalidDataException("RAW_DIGEST_MISMATCH");
        var pub=input.Publication;
        if(pub.Status is not ("UNKNOWN" or "SOURCE_CLAIM_UNREVIEWED")||pub.Status=="UNKNOWN"&&(pub.ClaimedPublishedAt is not null||pub.OriginalEvidenceDigest is not null)||
            pub.Status=="SOURCE_CLAIM_UNREVIEWED"&&(pub.ClaimedPublishedAt is null||pub.ClaimedPublishedAt.Value.Offset!=TimeSpan.Zero||pub.OriginalEvidenceDigest is null))throw new InvalidDataException("PUBLICATION_OBSERVATION_INVALID");
        if(pub.OriginalEvidenceDigest is not null)Digest(pub.OriginalEvidenceDigest);
    }
    private CaptureHistory Read(CapturePlan p)
    {
        ValidatePlan(p);var folder=Folder(p.Id);var runs=new List<CaptureRun>();
        if(!Directory.Exists(folder))return SealHistory(p,[]);
        var saved=HistoricalDatasetStore.Read<CapturePlan>(Path.Combine(folder,"plan.json"));if(ResearchMath.Digest(saved)!=ResearchMath.Digest(p))throw new InvalidDataException("CAPTURE_PLAN_CHANGED");
        foreach(var dir in Directory.GetDirectories(folder).Where(d=>Regex.IsMatch(Path.GetFileName(d),"^[0-9]{8}$")).Order(StringComparer.Ordinal))
        {
            var input=HistoricalDatasetStore.Read<CaptureInput>(Path.Combine(dir,"input.json"));ValidateInput(p,input);
            var run=HistoricalDatasetStore.Read<CaptureRun>(Path.Combine(dir,"receipt.json"));var previous=runs.LastOrDefault();
            var expected=Make(input,p,previous);
            if(Path.GetFileName(dir)!=$"{runs.Count+1:D8}"||ResearchMath.Digest(run)!=ResearchMath.Digest(expected)||runs.Any(x=>x.IdempotencyKey==run.IdempotencyKey)||
                previous is not null&&run.RetrievedAt<previous.RetrievedAt)throw new InvalidDataException("CAPTURE_CHAIN_CHANGED");
            runs.Add(run);
        }
        return SealHistory(p,runs.ToArray());
    }
    private static CaptureHistory SealHistory(CapturePlan p,CaptureRun[] runs)
    {var h=new CaptureHistory(p.Id,runs,p.Fixture?"Fixture":"Snapshot","");return h with{Digest=ResearchMath.Digest(h)};}
    private static CaptureRun Make(CaptureInput input,CapturePlan p,CaptureRun? previous)
    {
        var changed=previous is not null&&previous.RawDigest!=input.Raw.SourceChecksum;
        var run=new CaptureRun((previous?.Sequence??0)+1,(previous?.VersionSequence??1)+(changed?1:0),p.Id,input.IdempotencyKey,ResearchMath.Digest(input),input.Raw.SourceChecksum,input.Raw.ArchiveId,
            input.Raw.ObservedAt,input.Raw.RetrievedAt,input.DecisionAt,input.Publication,p.Fixture,changed,input.Raw.RetrievedAt.AddDays(p.MinimumRetentionDays),previous?.Digest??"","");
        return run with{Digest=ResearchMath.Digest(run)};
    }
    public CaptureRun Append(CapturePlan p,CaptureInput input,CapturePermission[] permissions,IReadOnlySet<string> trusted,DateTimeOffset now)
    {
        ValidateInput(p,input);
        if(now.Offset!=TimeSpan.Zero||input.Raw.RetrievedAt>now||AuthorizationBlockers(p,permissions,trusted,now,input.Raw.ObservedAt).Length>0)throw new InvalidOperationException("CAPTURE_AUTHORIZATION_OR_CLOCK_BLOCKED");
        AcquisitionSafety.RequireCurrentSources(root);
        var folder=Folder(p.Id);Directory.CreateDirectory(folder);using var gate=AcquisitionSafety.Lease(folder);
        var planPath=Path.Combine(folder,"plan.json");if(!File.Exists(planPath))HistoricalDatasetStore.WriteNew(planPath,p);
        var history=Read(p);var existing=history.Runs.SingleOrDefault(x=>x.IdempotencyKey==input.IdempotencyKey);
        if(existing is not null){if(existing.InputDigest!=ResearchMath.Digest(input))throw new InvalidDataException("CAPTURE_IDEMPOTENCY_CONFLICT");return existing;}
        var previous=history.Runs.LastOrDefault();if(previous is not null&&input.Raw.RetrievedAt<previous.RetrievedAt)throw new InvalidDataException("CAPTURE_OBSERVATION_BACKDATED");
        var run=Make(input,p,previous);var pending=Path.Combine(folder,".pending-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(pending);
        HistoricalDatasetStore.WriteNew(Path.Combine(pending,"input.json"),input);HistoricalDatasetStore.WriteNew(Path.Combine(pending,"receipt.json"),run);
        Directory.Move(pending,Path.Combine(folder,$"{run.Sequence:D8}"));return run;
    }
    public CaptureHistory Replay(CapturePlan p)
    {
        if(!Directory.Exists(Folder(p.Id))){ValidatePlan(p);return SealHistory(p,[]);}
        using var gate=AcquisitionSafety.Lease(Folder(p.Id));return Read(p);
    }
    public SourceArchive Raw(CapturePlan p,string key)
    {
        var history=Replay(p);var run=history.Runs.Single(x=>x.IdempotencyKey==key);
        var input=HistoricalDatasetStore.Read<CaptureInput>(Path.Combine(Folder(p.Id),$"{run.Sequence:D8}","input.json"));ValidateInput(p,input);
        if(ResearchMath.Digest(input)!=run.InputDigest)throw new InvalidDataException("CAPTURE_INPUT_CHANGED");return input.Raw;
    }
    public CaptureQualityReceipt Assess(CapturePlan p,string key,CaptureQualityContext context)
    {
        AcquisitionSafety.RequireCurrentSources(root);
        var run=Replay(p).Runs.Single(x=>x.IdempotencyKey==key);
        if(context.DecisionAt!=run.DecisionAt)throw new InvalidDataException("QUALITY_DECISION_SCOPE_MISMATCH");
        var raw=Raw(p,key);var request=Request(p,raw);var response=new OfficialDataProviderAdapter(p.Provider).Transform(request,raw);
        var result=new CaptureQualityReceipt(p.Id,run.Digest,context,CaptureQuality.Evaluate(request,response,context),"");result=result with{Digest=ResearchMath.Digest(result)};
        var folder=Folder(p.Id);using var gate=AcquisitionSafety.Lease(folder);var dir=Path.Combine(folder,"quality");Directory.CreateDirectory(dir);
        var path=Path.Combine(dir,result.Digest+".json");
        if(File.Exists(path)){if(ResearchMath.Digest(HistoricalDatasetStore.Read<CaptureQualityReceipt>(path))!=ResearchMath.Digest(result))throw new InvalidDataException("CAPTURE_QUALITY_CHANGED");}
        else {var temp=Path.Combine(dir,Guid.NewGuid().ToString("N")+".tmp");HistoricalDatasetStore.WriteNew(temp,result);File.Move(temp,path,false);}
        return result;
    }
    // Explicit isolated fixture integration uses the existing scheduler and production gate, never a new worker.
    public async Task<EtlDispatchResult> ProcessFixture(CapturePlan p,string key,TradingSessionCalendar calendar,string mode,CaptureQualityContext? qualityContext=null,CancellationToken token=default)
    {
        ValidatePlan(p);if(!p.Fixture)throw new InvalidOperationException("REAL_CAPTURE_REQUIRES_EXISTING_CONTROLLED_DEPLOYMENT_WORKFLOW");
        var checks=Phase4MOperations.RequiredChecks.Select(c=>new ReadinessCheck(c,"Verified","ISOLATED_FIXTURE_ONLY")).ToArray();
        var schedulerRoot=Path.Combine(root,"scheduler");
        if(mode is "Disabled" or "Dry-run")return await EtlProductionGate.Run(schedulerRoot,"unused",mode,checks,token);
        if(mode!="Approved-enabled")throw new ArgumentException("INVALID_MODE");
        var capture=Replay(p).Runs.Single(x=>x.IdempotencyKey==key);
        var quality=Assess(p,key,qualityContext??new(null,null,capture.DecisionAt,[]));
        if(quality.Quality.Status=="QUARANTINED")return new(mode,"QUARANTINED",false);
        var raw=Raw(p,key);var request=Request(p,raw);var scheduler=new EtlScheduler(schedulerRoot);
        var definition=new ScheduledJobDefinition("",request,EtlPipeline.Identity(request,raw.SourceUri,true),raw.RetrievedAt,p.RequestDate,calendar,true,3);
        definition=definition with{Id=EtlScheduler.Identity(definition)};scheduler.Register(definition,raw);
        return await EtlProductionGate.Run(schedulerRoot,definition.Id,mode,checks,token);
    }
}
