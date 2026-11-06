using System.Net;
using System.Text.Json.Nodes;
using WorkJournal.Application.Research;
using WorkJournal.Web.Services.HistoricalResearch;
public static class Phase4IChecks
{
    public static async Task Run(HttpClient http,Action<bool,string> check)
    {
        var paths=new[]{"/api/v1/authority/closure-queue","/api/v1/authority/claims/Availability:SourceIdentity/evidence","/api/v1/research/datasets/missing/readiness","/api/v1/research/runs/REAL_BASELINE_V1/validation-summary","/api/v1/research/runs/REAL_BASELINE_V1/replay-status"};
        http.DefaultRequestHeaders.Remove("X-Test-User");
        foreach(var path in paths){var response=await http.GetAsync(path);check(response.StatusCode==HttpStatusCode.Unauthorized&&response.Headers.Contains("X-Trace-Id"),"Phase4I session and trace "+path);}
        http.DefaultRequestHeaders.Add("X-Test-User","alice");
        async Task<JsonNode> Get(string path,int expected=200){var response=await http.GetAsync(path);check((int)response.StatusCode==expected&&response.Headers.Contains("X-Trace-Id"),"Phase4I HTTP "+expected+" "+path);var j=JsonNode.Parse(await response.Content.ReadAsStringAsync())!;if(expected>=400)check(j["code"] is not null&&j["traceId"] is not null,"Phase4I ProblemDetails");return j;}
        var queue=await Get(paths[0]+"?pageSize=200");var items=queue["claims"]!["items"]!.AsArray();check(items.Count==20&&items.Select(c=>c!["id"]!.GetValue<string>()).Distinct().Count()==20,"20 scoped unique claim keys");
        check(items[0]!["claim"]!["priority"]!.GetValue<int>()==0,"historical publication/source identity prioritized");
        var detail=await Get(paths[1]);check(detail["requirement"]!["decisions"]!.AsArray().Count>0,"claim evidence includes reviewed raw identity");
        check(!detail.ToJsonString().Contains("raw.bin")&&!detail.ToJsonString().Contains("C:\\"),"no evidence archive paths");
        await Get("/api/v1/authority/claims/SourceIdentity/evidence",400);await Get("/api/v1/authority/claims/missing/evidence",404);
        await Get(paths[0]+"?page=0",400);await Get(paths[0]+"?domain=unknown",400);
        var filtered=await Get(paths[0]+"?domain=Availability&pageSize=1");check(filtered["claims"]!["totalCount"]!.GetValue<int>()==3&&filtered["claims"]!["items"]!.AsArray().Count==1,"closure queue filtering and pagination");
        var summary=await Get(paths[3]);check(summary["trend"]!["status"]!.GetValue<string>()=="NOT_RUN"&&summary["trend"]!["metrics"] is null&&summary["trend"]!["ic"] is null,"unapproved baseline has null performance");
        var provenance=await Get("/api/v1/research/runs/REAL_BASELINE_V1/provenance");var id=provenance["datasetSnapshotId"]!.GetValue<string>();
        var ready=await Get("/api/v1/research/datasets/"+id+"/readiness");var counts=ready["counts"]!;
        check(counts["totalFacts"]!.GetValue<int>()==21&&counts["priceFacts"]!.GetValue<int>()==20&&counts["institutionalFacts"]!.GetValue<int>()==1,"21 facts reconciled to 20 price plus 1 institutional");
        check(counts["duplicateFacts"]!.GetValue<int>()==0&&counts["requestedObservations"]!.GetValue<int>()==1&&counts["approvedResearchSamples"]!.GetValue<int>()==0,"facts observations and approved sample denominators separated");
        var replay=await Get(paths[4]);check(replay["reportIntegrity"]!.GetValue<string>()=="VERIFIED_ON_READ"&&replay["replay"]!["baselineStatus"]!.GetValue<string>()=="NOT_RUN","report replay does not fabricate baseline replay");
        await Get(paths[2],404);await Get("/api/v1/research/runs/missing/replay-status",404);await Get("/api/v1/research/runs/missing/validation-summary",404);
        var post=await http.PostAsync(paths[0],new StringContent("{}"));check(post.StatusCode==HttpStatusCode.MethodNotAllowed,"no public authority approval writes");
        var report=new Phase4IReadService(Path.Combine(AppContext.BaseDirectory,"Research","phase4i-state.json")).Read();
        void Reject(Phase4IReport bad,string name){try{Phase4IProjection.Validate(bad);}catch(InvalidDataException){check(true,name);return;}throw new Exception(name);}
        Reject(report with{Digest="changed"},"Phase4I report tamper rejected");
        Reject(Phase4IProjection.Seal(report with{Counts=report.Counts with{TotalFacts=20}}),"resealed wrong denominator rejected");
        Reject(Phase4IProjection.Seal(report with{Trend=report.Trend with{Status="COMPLETED"}}),"resealed fake completed baseline rejected");
        Reject(Phase4IProjection.Seal(report with{Trend=report.Trend with{Status="INSUFFICIENT_SAMPLE"}}),"unapproved dataset cannot claim sample-only blockage");
        var facts=report.Facts.Append(report.Facts[0]).ToArray();var dedup=Phase4IProjection.Count(facts,report.Validation);check(dedup.DuplicateFacts==1&&dedup.UniqueFacts==21,"exact duplicate fact cannot inflate unique denominator");
        var revision=Phase4IProjection.Count(report.Facts.Append(report.Facts[0] with{FactChecksum=new string('A',64),RevisionId="v2"}).ToArray(),report.Validation);check(revision.AdditionalRevisions==1&&revision.DuplicateFacts==0,"different revision distinguished from duplicate");
        check(report.Validation.Observations.All(o=>o.AvailableAt is null)&&report.Decisions.All(d=>d.ReviewedAt!=default),"retrieved and reviewed timestamps never substituted for historical availability");
        var html=await http.GetStringAsync("/ResearchValidation");check(html.Contains("research-phase4i.js")&&html.Contains("validation-claims")&&html.Contains("validation-pit"),"Phase4I website uses formal API views");
    }
}
