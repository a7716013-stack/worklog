using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using WorkJournal.Application.Research;
using WorkJournal.Domain.Research;
using WorkJournal.Web.Services.HistoricalResearch;
using EvidenceValidationReport = WorkJournal.Application.Research.EvidenceValidationReport;
public static class ValidationChecks
{
    public static async Task Run(HttpClient http,Action<bool,string> check)
    {
        var paths=new[]{"/api/v1/research/eligibility-summary","/api/v1/research/runs/REAL_BASELINE_V1/provenance","/api/v1/pit/observations/missing/eligibility","/api/v1/research/datasets/missing/coverage"};
        http.DefaultRequestHeaders.Remove("X-Test-User");
        foreach(var path in paths){var r=await http.GetAsync(path);check(r.StatusCode==HttpStatusCode.Unauthorized&&r.Headers.Contains("X-Trace-Id"),"validation read requires session with trace "+path);}
        http.DefaultRequestHeaders.Add("X-Test-User","alice");
        async Task<JsonNode> Get(string path,int code=200){var r=await http.GetAsync(path);check((int)r.StatusCode==code&&r.Headers.Contains("X-Trace-Id"),"validation HTTP "+code+" "+path);var node=JsonNode.Parse(await r.Content.ReadAsStringAsync())!;if(code>=400)check(node["code"] is not null&&node["traceId"] is not null,"validation error stable code/trace");return node;}
        var s=await Get(paths[0]);check(s["status"]!.GetValue<string>()=="BLOCKED","real research is blocked");
        check(s["components"]!.AsArray().Count==6&&s["components"]!.AsArray().Sum(c=>c!["weight"]!.GetValue<int>())==100,"six independent component scopes preserve weights");
        check(s["dataset"]!["eligibleObservations"]!.GetValue<int>()==0&&s["phase5"]!.GetValue<string>()=="LOCKED","no fabricated real milestone");
        var p=await Get(paths[1]);check(p["researchStatus"]!.GetValue<string>()=="NOT_RUN"&&p["researchOutputDigest"] is null&&p["datasetApprovalId"] is null,"planned run provenance is not executed research");
        check(p["formulaDigest"]!.GetValue<string>()==FormulaCatalog.Identity.FormulaDigest&&p["sourceReportDigest"]!.GetValue<string>()==s["sourceReportDigest"]!.GetValue<string>(),"provenance shares exact formula and source digest");
        var dataset=p["datasetSnapshotId"]!.GetValue<string>();
        var coverage=await Get("/api/v1/research/datasets/"+dataset+"/coverage?pageSize=1");
        var obs=coverage["observations"]!["items"]!.AsArray();check(obs.Count==1,"coverage observations paginated");
        var id=obs[0]!["observationId"]!.GetValue<string>();var o=await Get("/api/v1/pit/observations/"+id+"/eligibility");
        check(o["availableAt"] is null&&!o["eligible"]!.GetValue<bool>()&&o["gates"]!.AsArray().Count==7,"PIT unknown availableAt remains null with seven gates");
        check(!p.ToJsonString().Contains("C:\\")&&!o.ToJsonString().Contains("raw.bin"),"API does not expose archive paths");
        await Get("/api/v1/research/datasets/"+dataset+"/coverage?page=0",400);
        await Get("/api/v1/research/datasets/"+dataset+"/coverage?from=2026-10-01&to=2026-09-01",400);
        var empty=await Get("/api/v1/research/datasets/"+dataset+"/coverage?from=2099-01-01");check(empty["observations"]!["totalCount"]!.GetValue<int>()==0,"date range does not invent observations");
        await Get(paths[2],404);await Get(paths[3],404);await Get("/api/v1/research/runs/missing/provenance",404);
        var post=await http.PostAsync(paths[0],new StringContent("{}"));check(post.StatusCode==HttpStatusCode.MethodNotAllowed,"eligibility cannot authorize public approval write");
        var report=HistoricalDatasetStore.Read<EvidenceValidationReport>(Path.Combine(AppContext.BaseDirectory,"Research","validation-state.json"));
        EvidenceValidationWorkflow.Validate(report);
        void Reject(EvidenceValidationReport invalid,string name){try{EvidenceValidationWorkflow.Validate(invalid);}catch(InvalidDataException){check(true,name);return;}throw new Exception(name);}
        Reject(report with{SourceReportDigest=new string('0',64)},"tampered release report blocked");
        Reject(EvidenceValidationWorkflow.Seal(report with{Provenance=report.Provenance with{ResearchStatus="COMPLETED"}}),"unsupported completed research fails closed even if resealed");
        Reject(EvidenceValidationWorkflow.Seal(report with{Observations=report.Observations.Select((x,i)=>i==0?x with{Eligible=true}:x).ToArray()}),"missing gates cannot be relabeled eligible");
        check(report.Claims.Length==20&&report.Claims.All(c=>c.Sources.Length>0),"20 claims retain scoped raw evidence provenance");
        check(FormulaCatalog.Identity.FormulaDigest=="81C788B359568B2B058EC92EDFE2F30A3593D7C2A23B3BE8C126BA026D06CBFB","3.1.1 immutable formula digest preserved");
        var missingReader=new WorkJournal.Api.Services.ValidationReadAdapter(new EvidenceValidationReadService(Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString("N"),"missing.json")));
        try{missingReader.Read();throw new Exception("Missing report accepted");}catch(WorkJournal.Api.ApiFault e){check(e.Status==503&&e.Code=="DATA_NOT_READY","missing report yields explicit not-ready");}
        var page=await http.GetAsync("/ResearchValidation");var markup=await page.Content.ReadAsStringAsync();
        check(page.IsSuccessStatusCode&&markup.Contains("research-validation.js")&&markup.Contains("validation-components"),"website renders API-backed validation view");
    }
}
