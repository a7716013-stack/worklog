using WorkJournal.Web.Services.HistoricalResearch;
using WorkJournal.Web.Services.Research;
namespace WorkJournal.Application.Research;

public sealed record ReviewActor(string Id,string[] Roles);
public sealed record ReviewCommand(string TaskId,string Action,string ExpectedDigest,string Operator,string Reviewer,string Reason,string? ReviewDigest);
public sealed record ReviewEvent(int Sequence,string PreviousDigest,string TaskId,string SourceVersion,string SubjectDigest,
    string Action,string Actor,string Operator,string Reviewer,string Reason,string? ReviewDigest,DateTimeOffset At,string Digest);
public sealed record ReviewQueueItem(string TaskId,string ClaimKey,string SourceVersion,string SubjectDigest,DateOnly CoverageStart,DateOnly CoverageEnd,
    string Operator,string Reviewer,string Status,string? Reason,string? ReviewDigest,string? ResolutionReceipt,string[] BlockingReasons,string HeadDigest);

// Controlled CLI only. OS ACL protects the store and operator roster; no HTTP write surface.
// A review is checked against the existing Phase4I receipt, never a second authority approval.
public sealed class AuthorityWorkbench(string root)
{
    private string Folder(string task){EvidenceAcquisitionStore.RequireDigest(task);return Path.Combine(root,"workbench",task);}
    public ReviewEvent[] History(AcquisitionWork task)
    {
        var dir=Folder(task.TaskId);if(!Directory.Exists(dir))return [];
        var events=Directory.GetFiles(dir,"*.json").Order(StringComparer.Ordinal).Select(HistoricalDatasetStore.Read<ReviewEvent>).ToArray();
        string previous="";DateTimeOffset at=DateTimeOffset.MinValue;
        for(var i=0;i<events.Length;i++) {var e=events[i];
            if(e.Sequence!=i+1||e.PreviousDigest!=previous||e.TaskId!=task.TaskId||e.SourceVersion!=task.SourceVersion||e.SubjectDigest!=task.SubjectDigest||
                e.At<at||e.At.Offset!=TimeSpan.Zero||e.Digest!=ResearchMath.Digest(e with{Digest=""})||
                e.Action is not ("ASSIGN" or "REVIEW" or "REJECT" or "REVOKE"))throw new InvalidDataException("REVIEW_HISTORY_INVALID");
            previous=e.Digest;at=e.At;
        }return events;
    }
    public ReviewEvent Append(Phase4JReport evidence,Phase4IReport parent,ReviewActor actor,ReviewCommand command,DateTimeOffset at)
    {
        Phase4JProjection.Validate(evidence,parent);
        if(string.IsNullOrWhiteSpace(actor.Id)||!actor.Roles.Any(r=>r is "Administrator" or "EvidenceReviewer" or "ResearchApprover"))throw new UnauthorizedAccessException();
        var task=evidence.Plan.Single(t=>t.TaskId==command.TaskId);
        var dir=Folder(task.TaskId);Directory.CreateDirectory(dir);using var gate=AcquisitionSafety.Lease(dir);
        var history=History(task);var head=history.LastOrDefault();
        if(command.ExpectedDigest!=(head?.Digest??""))throw new InvalidOperationException("STALE_REVIEW");
        if(at.Offset!=TimeSpan.Zero||at<(head?.At??DateTimeOffset.MinValue)||string.IsNullOrWhiteSpace(command.Reason)||command.Reason.Length>2000)throw new ArgumentException("REASON_AND_UTC_REQUIRED");
        var op=head?.Operator??"";var reviewer=head?.Reviewer??"";
        switch(command.Action) {
            case "ASSIGN":
                if(!actor.Roles.Any(r=>r is "Administrator" or "ResearchApprover"))throw new UnauthorizedAccessException();
                if(string.IsNullOrWhiteSpace(command.Operator)||string.IsNullOrWhiteSpace(command.Reviewer)||command.Operator==command.Reviewer)throw new InvalidOperationException("INDEPENDENT_REVIEWER_REQUIRED");
                if(history.Any(e=>e.Action is "REVIEW" or "REVOKE"))throw new InvalidOperationException("REVIEWED_SCOPE_ASSIGNMENT_IMMUTABLE");
                op=command.Operator;reviewer=command.Reviewer;break;
            case "REVIEW": case "REJECT": case "REVOKE":
                if(head is null||actor.Id!=reviewer||actor.Id==op||!actor.Roles.Any(r=>r is "EvidenceReviewer" or "Administrator"))throw new UnauthorizedAccessException();
                if(command.Action=="REVOKE"&&!history.Any(e=>e.Action=="REVIEW"))throw new InvalidOperationException("REVIEW_REQUIRED_BEFORE_REVOCATION");
                if(history.Any(e=>e.Action=="REVOKE"))throw new InvalidOperationException("REVOKED_SCOPE_REQUIRES_NEW_SOURCE_REVIEW");
                if(command.Action=="REVIEW") {
                    if(evidence.Impacts.Any(i=>i.SourceId==task.SourceId&&i.SourceVersion==task.SourceVersion))throw new InvalidOperationException("SOURCE_INVALIDATED");
                    var receipt=parent.Decisions.SingleOrDefault(d=>d.ReviewDigest==command.ReviewDigest&&d.ClaimKey==task.ClaimKey&&d.RawDigest==task.SourceVersion);
                    if(receipt is null||receipt.Reviewer!=actor.Id||receipt.ReviewedAt>at||receipt.CoverageStart>task.CoverageStart||receipt.CoverageEnd<task.CoverageEnd)
                        throw new InvalidOperationException("EXISTING_SCOPED_REVIEW_RECEIPT_REQUIRED");
                    var ledger=Path.Combine(root,"resolution-queue");
                    var attempts=Directory.Exists(ledger)?Directory.GetFiles(ledger,"*.result.json",SearchOption.AllDirectories).Select(HistoricalDatasetStore.Read<WorkJournal.Web.Models.HistoricalResearch.Resolution.EvidenceAcquisitionAttempt>).ToArray():[];
                    if(attempts.Any(a=>a.Digest!=ResearchMath.Digest(a with{Digest=""})))throw new InvalidDataException("ACQUISITION_LEDGER_INVALID");
                    var matches=attempts.Where(a=>a.EvidenceId==receipt.EvidenceId&&a.RawDigest==receipt.RawDigest&&a.SourceReference==evidence.Sources.Single(s=>s.SourceId==task.SourceId&&s.SourceVersion==task.SourceVersion).OfficialUrl&&a.AcquisitionStatus=="HTTP_SUCCESS").ToArray();
                    if(matches.Length==0||matches.Any(a=>a.Operator==actor.Id)||!matches.Any(a=>a.Operator==op))throw new InvalidOperationException("ACQUISITION_OPERATOR_PROOF_OR_INDEPENDENCE_MISSING");
                }break;
            default:throw new ArgumentException("INVALID_REVIEW_ACTION");
        }
        var next=new ReviewEvent(history.Length+1,head?.Digest??"",task.TaskId,task.SourceVersion,task.SubjectDigest,command.Action,actor.Id,op,reviewer,
            command.Reason,command.Action=="REVIEW"?command.ReviewDigest:null,at,"");
        next=next with{Digest=ResearchMath.Digest(next)};
        // Publish invalidation FIRST: interrupted revoke fails closed, never leaves an approved scope usable.
        if(command.Action=="REVOKE") {
            var impact=Phase4JProjection.Invalidate(evidence,task.SourceId,task.SourceVersion,"REVOKED").Single();
            var impacts=Path.Combine(root,"supply-chain");Directory.CreateDirectory(impacts);
            var path=Path.Combine(impacts,ResearchMath.Digest(impact)+".impact.json");
            if(!File.Exists(path))HistoricalDatasetStore.WriteNew(path,impact);
        }
        HistoricalDatasetStore.WriteNew(Path.Combine(dir,$"{next.Sequence:D8}.json"),next);return next;
    }
    public ReviewQueueItem[] Queue(Phase4JReport evidence,Phase4IReport parent)
    {
        Phase4JProjection.Validate(evidence,parent);
        return evidence.Plan.Select(t=>{
            var h=History(t).LastOrDefault();var claim=Phase4IProjection.Queue(parent).Single(c=>c.Id==t.ClaimKey);
            var impacts=Path.Combine(root,"supply-chain");
            var local=Directory.Exists(impacts)?Directory.GetFiles(impacts,"*.impact.json").Select(HistoricalDatasetStore.Read<SourceImpact>).ToArray():[];
            foreach(var impact in local)if(ResearchMath.Digest(impact)!=ResearchMath.Digest(Phase4JProjection.Invalidate(evidence,impact.SourceId,impact.SourceVersion,impact.Reason).Single()))throw new InvalidDataException("INVALID_LOCAL_REVOCATION");
            var invalid=evidence.Impacts.Concat(local).Any(i=>i.SourceId==t.SourceId&&i.SourceVersion==t.SourceVersion)||h?.Action=="REVOKE";
            return new ReviewQueueItem(t.TaskId,t.ClaimKey,t.SourceVersion,t.SubjectDigest,t.CoverageStart,t.CoverageEnd,h?.Operator??"UNASSIGNED",h?.Reviewer??"UNASSIGNED",
                invalid?"REVOKED":h?.Action=="REVIEW"?"REVIEWED_SCOPE_ONLY":h?.Action=="REJECT"?"REJECTED":h is null?"UNASSIGNED":"AWAITING_REVIEW",h?.Reason,h?.ReviewDigest,
                invalid?null:claim.Claim.ResolutionReceiptDigest,t.BlockingReasons.Concat(invalid?["SOURCE_REVOKED_REEVALUATE_AUTHORITY_PIT_DATASET"]:Array.Empty<string>()).ToArray(),h?.Digest??"");
        }).ToArray();
    }
}
