using System.Text.Json.Nodes;
using WorkJournal.Application.Research;
using WorkJournal.Web.Services.HistoricalResearch;
using WorkJournal.Web.Services.Research;
public static class Phase4NChecks
{
    public static async Task Run(HttpClient http,Action<bool,string> check)
    {
        var p=HistoricalDatasetStore.Read<Phase4IReport>("docs/phase4i-state-3.2.2.json");var e=HistoricalDatasetStore.Read<Phase4JReport>("docs/phase4j-state-3.2.2.json");
        var m=HistoricalDatasetStore.Read<Phase4MReport>("docs/phase4m/phase4m-state-3.2.2.json");var sha=new string('a',40);var now=DateTimeOffset.UtcNow;
        var report=Phase4NOperations.Create(p,e,m,sha,now);Phase4NOperations.Validate(report,p,e,m);
        check(report.Packets.Length==43&&report.Packets.Select(x=>x.TaskId).Distinct().Count()==43,"4N exact existing acquisition tasks, no approvals");
        check(report.Packets.All(x=>x.AssignedReviewer is null)&&report.Packets.All(x=>x.OriginalPublicationEvidence.Contains("UNPROVEN")),"4N no fabricated reviewer or publication time");
        check(report.Baseline.Status=="BLOCKED_BY_AUTHORITY"&&report.Baseline.ExecutionStatus=="NOT_RUN"&&report.Counts.EligibleObservations==0,"4N existing authority state blocks baseline");
        check(report.Production.Mode=="Disabled"&&!report.Production.CanDispatch&&report.DeploymentEvidence.All(x=>x.Result=="BLOCKED"),"4N deployment remains disabled with 0/8 verified gates");
        check(report.ProductionAcceptance.All(x=>x.Result=="BLOCKED"),"4N no invented live member acceptance");
        void Reject(Action a,string name){try{a();}catch(InvalidDataException){check(true,name);return;}throw new Exception("Expected rejection "+name);}
        Reject(()=>Phase4NOperations.Validate(report with{Counts=report.Counts with{EligibleObservations=10}},p,e,m),"4N tampered counts rejected");
        var altered=report with{Production=report.Production with{CanDispatch=true}};altered=altered with{Digest=ResearchMath.Digest(altered with{Digest=""})};
        Reject(()=>Phase4NOperations.Validate(altered,p,e,m),"4N rehashed enable attempt rejected");
        var env=Phase4NOperations.EnvironmentName;
        var valid=report.DeploymentEvidence.Select(x=>x with{Result="PASS",EvidenceType="REVIEWED_DEPLOYMENT_RECEIPT",EvidenceDigest=null,VerifiedAt=now.AddMinutes(-1),ExpiresAt=now.AddDays(1),Reviewer="fixture-reviewer"}).Select(x=>x with{EvidenceDigest=ResearchMath.Digest(x)}).ToArray();
        var trusted=valid.Select(x=>x.EvidenceDigest!).ToHashSet();
        bool All(DeploymentEvidence[] records,IReadOnlySet<string>? trust=null)=>Phase4NOperations.AssessEvidence(records,env,sha,now,trust??trusted).All(x=>x.Status=="Verified");
        check(All(valid),"4N fixture trusted scoped current receipts are assessable without dispatch");
        check(!All(valid,new HashSet<string>()),"4N self asserted PASS digest is not trusted");
        foreach(var changed in new[]{valid[0] with{Environment="other"},valid[0] with{SourceCommit=new string('b',40)},valid[0] with{ExpiresAt=now},valid[0] with{VerifiedAt=now.AddDays(1)},valid[0] with{Reviewer=null},valid[0] with{EvidenceType="Fixture"},valid[0] with{ExpiresAt=now.AddDays(31)},valid[0] with{EvidenceDigest="bad"}})
        {
            var resealed=changed with{EvidenceDigest=null};resealed=resealed with{EvidenceDigest=ResearchMath.Digest(resealed)};
            var trust=new HashSet<string>(trusted){resealed.EvidenceDigest!};
            if(changed.EvidenceDigest=="bad")check(!All(new[]{changed}.Concat(valid.Skip(1)).ToArray()),"4N corrupt receipt digest rejected");
            else check(!All(new[]{resealed}.Concat(valid.Skip(1)).ToArray(),trust),"4N trusted but wrong scope/stale/fixture/incomplete receipt blocked");
        }
        check(!All(valid.Skip(1).ToArray()),"4N missing gate blocked");
        check(!All(new[]{valid[0] with{Code=valid[1].Code}}.Concat(valid.Skip(2)).ToArray()),"4N receipt cannot be reused for a different gate");
        var revokedQueue=m.ReviewQueue.Select((q,i)=>i==0?q with{Status="REVOKED",BlockingReasons=["SOURCE_REVOKED_REEVALUATE_AUTHORITY_PIT_DATASET"]}:q).ToArray();
        var revoked=m with{ReviewQueue=revokedQueue,Trend=Phase4MOperations.Trend(p,e,revokedQueue)};revoked=revoked with{Digest=ResearchMath.Digest(revoked with{Digest=""})};
        var revokedReport=Phase4NOperations.Create(p,e,revoked,sha,now);
        check(revokedReport.Packets.Single(x=>x.TaskId==revokedQueue[0].TaskId).RevocationImpact.Contains("REVIEW_REVOKED")&&revokedReport.Baseline.Status=="BLOCKED_BY_AUTHORITY","4N existing revocation reaches packet and baseline");
        Reject(()=>Phase4NOperations.AssessEvidence([valid[0],valid[0]],env,sha,now,trusted),"4N duplicate gate rejected");
        // Pure display mapping over existing stage states; fixtures do not authorize a dataset.
        var trend=m.Trend;TrendFactoryReport Stages(string auth,string dataset,string sample,string baseline)=>trend with{Stages=trend.Stages.Select(s=>s.Name switch{"Authority"=>s with{Status=auth},"Dataset Approval"=>s with{Status=dataset},"Sample Sufficiency"=>s with{Status=sample},_=>s}).ToArray(),Baseline=baseline};
        check(Phase4NOperations.Decide(Stages("VERIFIED","BLOCKED","NOT_ASSESSED","NOT_RUN")).Status=="APPROVED_DATASET_REQUIRED","4N existing approval required mapping");
        check(Phase4NOperations.Decide(Stages("VERIFIED","APPROVED","INSUFFICIENT_SAMPLE","INSUFFICIENT_SAMPLE")).Status=="INSUFFICIENT_SAMPLE","4N insufficient sample mapping");
        check(Phase4NOperations.Decide(Stages("VERIFIED","APPROVED","NOT_ASSESSED","NOT_RUN")).Status=="NOT_RUN","4N no invented readiness before sample assessment");
        check(Phase4NOperations.Decide(Stages("VERIFIED","APPROVED","SUFFICIENT","COMPLETED")).Status=="READY","4N existing completed sufficient assessment mapping");
        foreach(var path in new[]{"operations/dashboard","authority/resolution-packets"})check((int)(await http.GetAsync("/api/v1/"+path)).StatusCode==403,"4N member cannot read "+path);
        http.DefaultRequestHeaders.Add("X-Test-Role","Administrator");
        try {
            var dashboard=await http.GetAsync("/api/v1/operations/dashboard");check(dashboard.IsSuccessStatusCode&&dashboard.Headers.CacheControl?.NoStore==true,"4N admin dashboard no-store");
            var q=JsonNode.Parse(await http.GetStringAsync("/api/v1/authority/resolution-packets?pageSize=200"))!;check(q["totalCount"]!.GetValue<int>()==43,"4N admin sees 43 packets");
            check((int)(await http.GetAsync("/api/v1/authority/resolution-packets?pageSize=201")).StatusCode==400,"4N bounded packet pagination");
            check((int)(await http.PostAsync("/api/v1/operations/dashboard",new StringContent("{}"))).StatusCode==405,"4N no deployment approval write API");
        }finally{http.DefaultRequestHeaders.Remove("X-Test-Role");}
        var user=http.DefaultRequestHeaders.GetValues("X-Test-User").Single();http.DefaultRequestHeaders.Remove("X-Test-User");
        try{foreach(var path in new[]{"operations/dashboard","authority/resolution-packets"})check((int)(await http.GetAsync("/api/v1/"+path)).StatusCode==401,"4N anonymous denied "+path);}
        finally{http.DefaultRequestHeaders.Add("X-Test-User",user);}
    }
}
