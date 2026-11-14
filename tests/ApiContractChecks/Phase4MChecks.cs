using System.Text.Json.Nodes;
using WorkJournal.Application.Research;
using WorkJournal.Web.Services.HistoricalResearch;
using WorkJournal.Web.Services.Research;
public static class Phase4MChecks
{
    public static async Task Run(HttpClient http,Action<bool,string> check)
    {
        var p=HistoricalDatasetStore.Read<Phase4IReport>("docs/phase4i-state-3.2.2.json");var e=HistoricalDatasetStore.Read<Phase4JReport>("docs/phase4j-state-3.2.2.json");
        var root=Path.Combine(Path.GetTempPath(),"phase4m-"+Guid.NewGuid().ToString("N"));var work=new AuthorityWorkbench(root);
        void Reject(Action action,string name){try{action();}catch(Exception ex) when(ex is InvalidOperationException or InvalidDataException or ArgumentException or UnauthorizedAccessException or IOException){check(true,name);return;}throw new Exception("Expected rejection: "+name);}
        var q=work.Queue(e,p);check(q.Length==43&&q.All(t=>t.Status=="UNASSIGNED"),"4M existing 43 tasks remain unassigned, no fabricated reviewer");
        var task=e.Plan.First(t=>p.Decisions.Any(d=>d.ClaimKey==t.ClaimKey&&d.RawDigest==t.SourceVersion));
        var receipt=p.Decisions.First(d=>d.ClaimKey==task.ClaimKey&&d.RawDigest==task.SourceVersion);var reviewer=receipt.Reviewer;
        var admin=new ReviewActor("fixture-admin",["Administrator"]);var member=new ReviewActor("member",[]);var r=new ReviewActor(reviewer,["EvidenceReviewer"]);var at=DateTimeOffset.UtcNow;
        var assign=new ReviewCommand(task.TaskId,"ASSIGN","","fixture-operator",reviewer,"Fixture independent assignment",null);
        Reject(()=>work.Append(e,p,member,assign,at),"4M member cannot assign or review");
        Reject(()=>work.Append(e,p,admin,assign with{Operator=reviewer},at),"4M self-review assignment forbidden");
        var assigned=work.Append(e,p,admin,assign,at);
        var review=assign with{Action="REVIEW",ExpectedDigest=assigned.Digest,ReviewDigest=receipt.ReviewDigest};
        Reject(()=>work.Append(e,p,new("fixture-operator",["EvidenceReviewer"]),review,at),"4M operator cannot approve own acquisition");
        Reject(()=>work.Append(e,p,r,review with{ReviewDigest=new string('A',64)},at),"4M fabricated review receipt rejected");
        Reject(()=>work.Append(e,p,r,review with{ExpectedDigest=""},at),"4M stale review conflict rejected");
        Reject(()=>work.Append(e,p,r,review,at),"4M unknown acquisition operator cannot be approved by assignment alone");
        var acquisitionId=Guid.NewGuid();var attemptsDir=Path.Combine(root,"resolution-queue",acquisitionId.ToString("N"),"attempts");Directory.CreateDirectory(attemptsDir);
        var acquisition=new WorkJournal.Web.Models.HistoricalResearch.Resolution.EvidenceAcquisitionAttempt(Guid.NewGuid(),acquisitionId,e.Sources.Single(s=>s.SourceId==task.SourceId&&s.SourceVersion==task.SourceVersion).OfficialUrl,"FIXTURE",at,at,"HTTP_SUCCESS","application/json",receipt.RawDigest,null,WorkJournal.Web.Models.HistoricalResearch.Resolution.ResolutionStatus.CAPTURED,null,"fixture-operator",receipt.EvidenceId,"");
        acquisition=acquisition with{Digest=ResearchMath.Digest(acquisition)};HistoricalDatasetStore.WriteNew(Path.Combine(attemptsDir,acquisition.AttemptId+".result.json"),acquisition);
        var reviewed=work.Append(e,p,r,review,at);check(work.History(task).Length==2,"4M append-only review binds existing receipt");
        var revoked=work.Append(e,p,r,review with{Action="REVOKE",ExpectedDigest=reviewed.Digest,Reason="Fixture source revocation"},at);
        check(work.Queue(e,p).Single(x=>x.TaskId==task.TaskId).Status=="REVOKED","4M revoked review cannot retain closure");
        Reject(()=>AcquisitionSafety.RequireCurrentSources(root),"4M revocation reaches existing acquisition/dataset guard");
        check(Phase4MOperations.Trend(p,e,work.Queue(e,p)).Baseline=="NOT_RUN","4M revocation blocks baseline projection");
        Reject(()=>work.Append(e,p,r,review with{ExpectedDigest=revoked.Digest},at),"4M revoked scope cannot be silently rereviewed");
        var backup=root+"-backup";var restore=root+"-restore";
        var copied=OperationalBackup.CopyAndVerify(root,backup);OperationalBackup.CopyAndVerify(backup,restore);
        check(copied.Files.Length>0&&ResearchMath.Digest(new AuthorityWorkbench(restore).Queue(e,p))==ResearchMath.Digest(work.Queue(e,p)),"4M backup restore replays identical review and revocation receipts");
        Reject(()=>OperationalBackup.CopyAndVerify(root,backup),"4M backup never overwrites existing destination");
        File.WriteAllText(Path.Combine(restore,"workbench",task.TaskId,"00000002.json"),"{}");
        Reject(()=>new AuthorityWorkbench(restore).History(task),"4M tampered review ledger fails closed");
        var checklist=Phase4MOperations.RequiredChecks.Select(c=>new ReadinessCheck(c,"Verified","FIXTURE_ONLY_RECEIPT")).ToArray();
        check(!Phase4MOperations.Readiness("Disabled",checklist).CanDispatch,"4M disabled mode never dispatches");
        check(!Phase4MOperations.Readiness("Dry-run",checklist).CanDispatch&&!Phase4MOperations.Readiness("Dry-run",checklist).ExternalAcquisitionAllowed,"4M dry-run performs no dispatch/acquisition");
        check(!Phase4MOperations.Readiness("Approved-enabled",checklist.Skip(1).ToArray()).CanDispatch,"4M enabled requires every deployment checklist item");
        check(Phase4MOperations.Readiness("Approved-enabled",checklist).CanDispatch,"4M all explicit readiness requirements assessed without starting worker");
        var noIo=root+"-dry-run";
        check((await EtlProductionGate.Run(noIo,"unused","Dry-run",checklist)).Status=="DRY_RUN_NO_IO"&&!Directory.Exists(noIo),"4M dry-run creates no files and invokes no worker");
        check((await EtlProductionGate.Run(noIo,"unused","Disabled",[])).Status=="DISABLED"&&!Directory.Exists(noIo),"4M disabled worker creates no state");
        Reject(()=>EtlProductionGate.Run(noIo,"unused","Approved-enabled",[]).GetAwaiter().GetResult(),"4M dispatch rejects missing production approvals");
        var other=e.Plan.First(t=>t.TaskId!=task.TaskId);var commands=Enumerable.Range(0,4).Select(_=>Task.Run(()=>{try{work.Append(e,p,admin,assign with{TaskId=other.TaskId},at);return true;}catch(Exception ex) when(ex is IOException or InvalidOperationException){return false;}}));
        check((await Task.WhenAll(commands)).Count(x=>x)==1,"4M concurrent assignment has one append-only winner");
        var changed=Phase4IProjection.Seal(p with{Trend=p.Trend with{Policy=new("TrendSample-v1",19,10,200)}});
        Reject(()=>Phase4MOperations.Trend(changed,Phase4JProjection.Create(changed,e.Attempts,e.Impacts,e.GeneratedAt),[]),"4M rejects reduced Trend sample thresholds");
        var publicReport=JsonNode.Parse(await http.GetStringAsync("/api/v1/operations/readiness"))!;
        check(publicReport["phase"]!.GetValue<string>()=="4M"&&!publicReport["production"]!["canDispatch"]!.GetValue<bool>(),"4M API exposes disabled production readiness");
        check((int)(await http.GetAsync("/api/v1/authority/workbench")).StatusCode==403,"4M member cannot read management queue");
        http.DefaultRequestHeaders.Add("X-Test-Role","Administrator");
        try {var queue=JsonNode.Parse(await http.GetStringAsync("/api/v1/authority/workbench"))!;check(queue["totalCount"]!.GetValue<int>()==43,"4M administrator can inspect existing review queue");}
        finally{http.DefaultRequestHeaders.Remove("X-Test-Role");}
        var user=http.DefaultRequestHeaders.GetValues("X-Test-User").Single();http.DefaultRequestHeaders.Remove("X-Test-User");
        try{foreach(var path in new[]{"operations/readiness","research/trend/readiness","authority/workbench","authority/acquisition-evidence"})check((int)(await http.GetAsync("/api/v1/"+path)).StatusCode==401,"4M anonymous 401 "+path);}
        finally{http.DefaultRequestHeaders.Add("X-Test-User",user);}
        check((int)(await http.PostAsync("/api/v1/authority/workbench",new StringContent("{}"))).StatusCode==405,"4M no public review/approval write endpoint");
    }
}
