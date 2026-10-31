using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WorkJournal.Web.Models.HistoricalResearch;
using WorkJournal.Web.Services.Research;
namespace WorkJournal.Web.Services.HistoricalResearch;

// Local operator tool: no scheduler, web endpoint or automatic evidence approval.
public sealed class EvidenceAcquisitionStore(string root)
{
    public static string BytesDigest(byte[] bytes)=>Convert.ToHexString(SHA256.HashData(bytes));
    private string ArtifactPath(Guid id)=>Path.Combine(root,"evidence","authority",id.ToString("N"));
    public async Task<EvidenceAcquisitionManifest> Acquire(AcquisitionPlan plan,HttpClient client,TimeProvider clock,CancellationToken token=default)
    {
        if(plan.AcquisitionRunId==Guid.Empty||string.IsNullOrWhiteSpace(plan.Provider)||plan.PeriodStart>plan.PeriodEnd||plan.Tasks.Length==0||
            plan.Tasks.Select(t=>t.TaskId).Distinct().Count()!=plan.Tasks.Length)throw new ArgumentException("Unique run/tasks and explicit period required.");
        foreach(var task in plan.Tasks)
        {
            if(task.TaskId==Guid.Empty||task.Provider!=plan.Provider||task.Status!=AcquisitionStatus.Pending||task.AttemptCount<0||
                !Enum.IsDefined(task.EvidenceDomain)||!Enum.IsDefined(task.RequiredEvidenceType)||string.IsNullOrWhiteSpace(task.DatasetType)||
                task.TargetPeriodStart<plan.PeriodStart||task.TargetPeriodEnd>plan.PeriodEnd||task.TargetPeriodEnd<task.TargetPeriodStart)
                throw new ArgumentException("Invalid acquisition task.");
            var uri=new Uri(task.SourceReference,UriKind.Absolute);
            if(uri.Scheme!="https"||uri.UserInfo.Length!=0||uri.Fragment.Length!=0||
               !(uri.Host=="www.twse.com.tw"||uri.Host=="wwwc.twse.com.tw"||uri.Host=="openapi.twse.com.tw"||uri.Host=="www.tpex.org.tw"))
                throw new ArgumentException("Controlled acquisition supports explicit public TWSE/TPEx HTTPS sources only.");
        }
        var directory=Path.Combine(root,"runs",plan.AcquisitionRunId.ToString("N"));Directory.CreateDirectory(directory);
        HistoricalDatasetStore.WriteNew(Path.Combine(directory,"plan.json"),plan); // Reserve before any network call.
        var started=clock.GetUtcNow();var captured=new List<EvidenceArtifact>();var failed=new List<AcquisitionFailure>();var tasks=new List<EvidenceAcquisitionTask>();
        foreach(var task in plan.Tasks)
        {
            var at=clock.GetUtcNow();
            var taskDir=Path.Combine(directory,"attempts");Directory.CreateDirectory(taskDir);
            HistoricalDatasetStore.WriteNew(Path.Combine(taskDir,task.TaskId.ToString("N")+".json"),
                task with{Status=AcquisitionStatus.Searching,AttemptCount=task.AttemptCount+1,LastAttemptAt=at,LastResult="REQUEST_STARTED"});
            try
            {
                using var response=await client.GetAsync(task.SourceReference,HttpCompletionOption.ResponseHeadersRead,token);
                if(!response.IsSuccessStatusCode)throw new InvalidDataException("HTTP_"+(int)response.StatusCode);
                // Redirects must be disabled by the CLI; do not archive a different source under the requested identity.
                if(response.RequestMessage?.RequestUri?.AbsoluteUri!=new Uri(task.SourceReference).AbsoluteUri)throw new InvalidDataException("SOURCE_REDIRECT");
                const int limit=16*1024*1024;
                if(response.Content.Headers.ContentLength>limit)throw new InvalidDataException("ARTIFACT_TOO_LARGE");
                await using var stream=await response.Content.ReadAsStreamAsync(token);using var buffer=new MemoryStream();
                var block=new byte[8192];int count;
                while((count=await stream.ReadAsync(block,token))!=0){if(buffer.Length+count>limit)throw new InvalidDataException("ARTIFACT_TOO_LARGE");buffer.Write(block,0,count);}
                var bytes=buffer.ToArray();if(bytes.Length==0)throw new InvalidDataException("EMPTY_BODY");
                var a=new EvidenceArtifact(Guid.NewGuid(),task.TaskId,task.Provider,task.DatasetType,task.EvidenceDomain,task.SourceReference,
                    clock.GetUtcNow(),response.Content.Headers.ContentType?.ToString()??"application/octet-stream",bytes.Length,BytesDigest(bytes),
                    "RawBytes-v1",task.TargetPeriodStart,task.TargetPeriodEnd,"");
                a=a with{MetadataDigest=ResearchMath.Digest(a)};
                var path=ArtifactPath(a.EvidenceId);Directory.CreateDirectory(path);
                using(var raw=new FileStream(Path.Combine(path,"raw.bin"),FileMode.CreateNew,FileAccess.Write)){raw.Write(bytes);raw.Flush(true);}
                HistoricalDatasetStore.WriteNew(Path.Combine(path,"metadata.json"),a);
                using(var sha=new FileStream(Path.Combine(path,"sha256.txt"),FileMode.CreateNew,FileAccess.Write)){sha.Write(Encoding.ASCII.GetBytes(a.SHA256));sha.Flush(true);}
                captured.Add(a);tasks.Add(task with{Status=AcquisitionStatus.Captured,AttemptCount=task.AttemptCount+1,LastAttemptAt=at,
                    LastResult="CAPTURED_NOT_VERIFIED",CapturedEvidenceId=a.EvidenceId,BlockingReason="REVIEW_REQUIRED"});
            }
            catch(Exception ex) when(ex is HttpRequestException or IOException or OperationCanceledException or InvalidDataException)
            {
                var reason=ex is InvalidDataException?ex.Message:ex.GetType().Name;
                failed.Add(new(task.TaskId,task.SourceReference,reason));tasks.Add(task with{Status=AcquisitionStatus.Blocked,
                    AttemptCount=task.AttemptCount+1,LastAttemptAt=at,LastResult=reason,BlockingReason=reason});
            }
        }
        var m=new EvidenceAcquisitionManifest(plan.AcquisitionRunId,started,clock.GetUtcNow(),plan.Provider,
            plan.Tasks.Select(t=>t.DatasetType).Distinct().Order(StringComparer.Ordinal).ToArray(),plan.PeriodStart,plan.PeriodEnd,
            captured.ToArray(),failed.ToArray(),ResearchMath.Digest(plan),"",failed.Count==0?"CAPTURED_NOT_VERIFIED":"PARTIAL_OR_BLOCKED",tasks.ToArray());
        m=m with{OutputDigest=ResearchMath.Digest(m)};HistoricalDatasetStore.WriteNew(Path.Combine(directory,"manifest.json"),m);return m;
    }
    public EvidenceArtifact ReadArtifact(Guid id)
    {
        var path=ArtifactPath(id);var a=HistoricalDatasetStore.Read<EvidenceArtifact>(Path.Combine(path,"metadata.json"));
        var bytes=File.ReadAllBytes(Path.Combine(path,"raw.bin"));
        if(a.EvidenceId!=id||a.ByteLength!=bytes.Length||a.SHA256!=BytesDigest(bytes)||a.MetadataDigest!=ResearchMath.Digest(a with{MetadataDigest=""})||
           File.ReadAllText(Path.Combine(path,"sha256.txt"))!=a.SHA256)throw new InvalidDataException("Evidence bytes or metadata changed.");
        return a;
    }
    public EvidenceAcquisitionManifest Replay(Guid id)
    {
        var dir=Path.Combine(root,"runs",id.ToString("N"));var m=HistoricalDatasetStore.Read<EvidenceAcquisitionManifest>(Path.Combine(dir,"manifest.json"));
        var plan=HistoricalDatasetStore.Read<AcquisitionPlan>(Path.Combine(dir,"plan.json"));
        if(m.AcquisitionRunId!=id||m.InputDigest!=ResearchMath.Digest(plan)||m.OutputDigest!=ResearchMath.Digest(m with{OutputDigest=""}))throw new InvalidDataException("Acquisition manifest changed.");
        foreach(var a in m.CapturedArtifacts)if(ResearchMath.Digest(ReadArtifact(a.EvidenceId))!=ResearchMath.Digest(a))throw new InvalidDataException("Acquisition artifact mismatch.");
        return m;
    }
    public EvidenceReviewReceipt Review(EvidenceReviewReceipt r)
    {
        var a=ReadArtifact(r.EvidenceId);ValidateReview(r,a);
        r=r with{ReviewDigest=ResearchMath.Digest(r with{ReviewDigest=""})};
        var dir=Path.Combine(root,"reviews");Directory.CreateDirectory(dir);
        HistoricalDatasetStore.WriteNew(Path.Combine(dir,r.ReviewDigest+".json"),r);return r;
    }
    public EvidenceReviewReceipt ReadReview(string digest)
    {
        RequireDigest(digest);var r=HistoricalDatasetStore.Read<EvidenceReviewReceipt>(Path.Combine(root,"reviews",digest+".json"));
        if(r.ReviewDigest!=digest||ResearchMath.Digest(r with{ReviewDigest=""})!=digest)throw new InvalidDataException("Review digest mismatch.");
        ValidateReview(r,ReadArtifact(r.EvidenceId));return r;
    }
    private static void ValidateReview(EvidenceReviewReceipt r,EvidenceArtifact a)
    {
        if(r.ReviewId==Guid.Empty||r.EvidenceDigest!=a.SHA256||r.ReviewedAt<a.CapturedAt||r.EffectiveFrom>r.EffectiveTo||
            r.EffectiveFrom<a.TargetStart||r.EffectiveTo>a.TargetEnd||
            new[]{r.Reviewer,r.Claim,r.Notes,r.Timezone}.Any(string.IsNullOrWhiteSpace)||
            r.Supported is null||r.Unsupported is null||r.Ambiguous is null||
            r.Supported.Intersect(r.Unsupported.Concat(r.Ambiguous)).Any()||r.Supported.Concat(r.Unsupported).Concat(r.Ambiguous).Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("Review must bind raw digest, temporal scope, explicit claims and reviewer.");
        TimeZoneInfo.FindSystemTimeZoneById(r.Timezone);
    }
    public static void RequireDigest(string digest)
    {if(digest.Length!=64||!digest.All(Uri.IsHexDigit))throw new ArgumentException("SHA256 required.");}
    public AcquisitionTaskRevision TaskState(Guid runId,Guid taskId)
    {
        var m=Replay(runId);var task=m.Tasks.Single(t=>t.TaskId==taskId);
        var current=new AcquisitionTaskRevision(0,"",task,m.CompletedAt,"acquisition",task.LastResult,null,"");
        current=current with{Digest=ResearchMath.Digest(current)};
        var path=Path.Combine(root,"runs",runId.ToString("N"),"tasks",taskId.ToString("N"));
        if(!Directory.Exists(path))return current;
        foreach(var file in Directory.GetFiles(path,"*.json").Order(StringComparer.Ordinal))
        {
            var next=HistoricalDatasetStore.Read<AcquisitionTaskRevision>(file);
            if(next.Sequence!=current.Sequence+1||next.PreviousDigest!=current.Digest||next.Digest!=ResearchMath.Digest(next with{Digest=""})||
                ResearchMath.Digest(next.Task with{Status=current.Task.Status,BlockingReason=current.Task.BlockingReason})!=ResearchMath.Digest(current.Task)||
                !Allowed(current.Task.Status,next.Task.Status)||next.At<current.At)throw new InvalidDataException("Acquisition review history mismatch.");
            if(next.Task.Status==AcquisitionStatus.Verified)ValidateTaskReview(next.Task,next.ReviewDigest,next.At);
            current=next;
        }
        return current;
    }
    public AcquisitionTaskRevision Transition(Guid runId,Guid taskId,string expected,AcquisitionStatus status,DateTimeOffset at,string reviewer,string reason,string? reviewDigest)
    {
        var current=TaskState(runId,taskId);
        if(current.Digest!=expected||!Allowed(current.Task.Status,status)||at<current.At||string.IsNullOrWhiteSpace(reviewer)||string.IsNullOrWhiteSpace(reason))
            throw new InvalidOperationException("Stale or invalid acquisition review transition.");
        if(status==AcquisitionStatus.Verified)ValidateTaskReview(current.Task,reviewDigest,at);
        var next=new AcquisitionTaskRevision(current.Sequence+1,current.Digest,current.Task with{Status=status,BlockingReason=status==AcquisitionStatus.Verified?"":reason},at,reviewer,reason,reviewDigest,"");
        next=next with{Digest=ResearchMath.Digest(next)};
        var path=Path.Combine(root,"runs",runId.ToString("N"),"tasks",taskId.ToString("N"));Directory.CreateDirectory(path);
        HistoricalDatasetStore.WriteNew(Path.Combine(path,next.Sequence.ToString("D8")+".json"),next);return next;
    }
    private void ValidateTaskReview(EvidenceAcquisitionTask task,string? digest,DateTimeOffset at)
    {
        var r=ReadReview(digest??"");
        if(r.EvidenceId!=task.CapturedEvidenceId||r.ReviewedAt>at||r.EffectiveFrom>task.TargetPeriodStart||r.EffectiveTo<task.TargetPeriodEnd||
            !RealDataApproval.RequiredClaims[task.EvidenceDomain].All(c=>r.Supported.Contains(c))||r.Ambiguous.Length>0||r.Unsupported.Length>0)
            throw new InvalidOperationException("Task verification requires a complete scoped review receipt.");
    }
    private static bool Allowed(AcquisitionStatus from,AcquisitionStatus to)=>(from,to) switch
    {
        (AcquisitionStatus.Captured,AcquisitionStatus.UnderReview)=>true,
        (AcquisitionStatus.UnderReview,AcquisitionStatus.Verified or AcquisitionStatus.Rejected or AcquisitionStatus.Blocked)=>true,
        (AcquisitionStatus.Verified,AcquisitionStatus.Superseded)=>true,
        _=>false // A failed acquisition retries under a NEW acquisition run identity.
    };
    public static AvailabilityResolution Resolve(VerifiedAvailabilityRule rule,EvidenceReviewReceipt review,DateOnly observation)
    {
        if(rule.Precision==AvailabilityPrecision.Unknown)return new(null,"AVAILABILITY_UNKNOWN",rule.AvailabilityResolutionPolicyVersion);
        if(!Enum.IsDefined(rule.Precision)||rule.ReviewDigest!=review.ReviewDigest||review.ReviewDigest!=ResearchMath.Digest(review with{ReviewDigest=""})||
            !review.Supported.Contains("Availability:"+rule.Version+":"+rule.Precision)||review.Unsupported.Length>0||review.Ambiguous.Length>0||
            observation<rule.EffectiveFrom||observation>rule.EffectiveTo||rule.EffectiveFrom<review.EffectiveFrom||rule.EffectiveTo>review.EffectiveTo||
            rule.AvailabilityResolutionPolicyVersion!="ConservativeUpperBound-v1"||rule.LowerBound is null||rule.UpperBound is null||rule.LowerBound>rule.UpperBound||
            (rule.Precision==AvailabilityPrecision.ExactTimestamp&&rule.LowerBound!=rule.UpperBound))
            return new(null,"UNSUPPORTED_PRECISION_OR_SCOPE",rule.AvailabilityResolutionPolicyVersion);
        // Even session/day rules require explicit evidence-backed bounds; never invent a market close time.
        return new(rule.UpperBound,"CONSERVATIVE_REVIEWED_UPPER_BOUND",rule.AvailabilityResolutionPolicyVersion);
    }
}
