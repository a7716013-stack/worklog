using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WorkJournal.Api;
using WorkJournal.Api.Contracts;
using WorkJournal.Api.Services;
using WorkJournal.Api.OpenApi;
using WorkJournal.Web.Data;
using WorkJournal.Web.Models;
using WorkJournal.Web.Services;
using WorkJournal.Web.Security;

int checks=0;
void Check(bool ok,string name) {if(!ok)throw new Exception(name);Console.WriteLine("PASS: "+name);checks++;}
var root=Directory.GetCurrentDirectory();
var app=ApiHost.Build(["--environment","Development","--StrategyCenter:Enabled","false","--RadarTracking:SchedulerEnabled","false","--InvestmentInsights:SchedulerEnabled","false"],b=> {
    b.WebHost.UseTestServer(); b.Logging.ClearProviders();
    b.Services.AddSingleton<StubRadar>();b.Services.AddScoped<IMarketRadarService>(sp=>sp.GetRequiredService<StubRadar>());
    b.Services.AddScoped<IMarketRadarHistoryProvider,StubHistory>();
    b.Services.AddScoped<IMarketIndustries,StubIndustries>();b.Services.AddScoped<IApiReadiness,StubReadiness>();
    b.Services.AddAuthentication(o=> {o.DefaultAuthenticateScheme="contract";o.DefaultChallengeScheme="contract";o.DefaultForbidScheme="contract";}).AddScheme<AuthenticationSchemeOptions,TestAuth>("contract",_=>{});
});
app.MapGet("/api/v1/test/forbidden",()=>"no").RequireAuthorization("ApiAuthorityAdmin");
app.MapGet("/api/v1/test/exception",(Func<string>)(()=>throw new Exception("SECRET_STACK_SENTINEL")));
Phase4GChecks.MapToken(app);
await app.StartAsync();var http=app.GetTestClient();http.BaseAddress=new Uri("https://localhost");
async Task<JsonNode> Get(string path,int status=200) {
    var response=await http.GetAsync(path);Check((int)response.StatusCode==status,path+" status "+status);
    Check(response.Headers.Contains("X-Trace-Id"),path+" trace header");
    var text=await response.Content.ReadAsStringAsync();var node=JsonNode.Parse(text)!;
    if(status>=400){Check(response.Content.Headers.ContentType?.MediaType=="application/problem+json","problem media type");Check(node["traceId"] is not null,"problem trace");Check(node["code"] is not null,"stable problem code");}
    return node;
}
Check((await Get("/api/v1/system/health"))["version"]!.GetValue<string>()=="3.1.1","release version");
Check((await Get("/api/v1/system/readiness"))["databaseReachable"]!.GetValue<bool>(),"readiness service");
await Get("/api/v1/not-found",404);
Check((await Get("/api/v1/paper/orders",401))["code"]!.GetValue<string>()=="AUTHENTICATION_REQUIRED","API no login redirect");
await Get("/api/v1/authority/coverage",401);
await Get("/api/v1/market/radar?market=invalid",400);
await Get("/api/v1/market/radar?date=oops",400);
await Get("/api/v1/market/radar?page=0",400);
await Get("/api/v1/market/radar?pageSize=201",400);
foreach(var market in new[]{"all","twse","tpex","etf"}) {
    var result=await Get("/api/v1/market/radar?market="+market+"&date=2026-09-30&analyze=true");
    Check(result["market"]!.GetValue<string>()==market,"market mapped");
    Check(result["quotes"]!["items"]![0]!["close"]!.GetValue<decimal>()==100.25m,"decimal preserved");
}
var legacy=await http.GetAsync("/StockAnalysis/MarketRadarData?market=twse&date=2026-09-30&analyze=true");
Check(legacy.IsSuccessStatusCode,"legacy MVC route preserved");
var legacyJson=JsonNode.Parse(await legacy.Content.ReadAsStringAsync())!;
Check(legacyJson["quotes"]![0]!["close"]!.GetValue<decimal>()==100.25m,"same market service legacy result");
var stub=app.Services.GetRequiredService<StubRadar>();stub.Failure=true;
Check((await Get("/api/v1/market/radar",503))["code"]!.GetValue<string>()=="UPSTREAM_UNAVAILABLE","provider outage mapping");stub.Failure=false;
using(var cancel=new CancellationTokenSource()){cancel.Cancel();try{await http.GetAsync("/api/v1/market/radar",cancel.Token);throw new Exception("not cancelled");}catch(OperationCanceledException){Check(true,"request cancellation");}}
var models=await Get("/api/v1/evidence/models");var model=models["items"]![0]!;
Check(model["v3Threshold"] is null&&model["thresholdStatus"]!.GetValue<string>()=="unresolved","missing threshold is not 70/75");
Check(model["components"]!.AsArray().Sum(x=>x!["weight"]!.GetValue<decimal>())==100,"shared six model weights");
await Get("/api/v1/evidence/models/unknown",404);
http.DefaultRequestHeaders.Add("X-Test-User","alice");
await Get("/api/v1/test/forbidden",403);
var error=await Get("/api/v1/test/exception",500);Check(!error.ToJsonString().Contains("SECRET_STACK_SENTINEL"),"exception sanitized");
var coverage=await Get("/api/v1/authority/coverage");Check(coverage["approvedAuthorityDomains"]!.GetValue<int>()==0,"zero approvals is valid");
Check((await Get("/api/v1/authority/claims"))["totalCount"]!.GetValue<int>()==20,"20 real claims");
Check((await Get("/api/v1/research/runs/REAL_BASELINE_V1/metrics"))["ic"] is null,"not run metrics null");
Check((await Get("/api/v1/research/dataset-approvals"))["totalCount"]!.GetValue<int>()==0,"no fabricated approvals");
await Get("/api/v1/evidence/"+Guid.NewGuid(),503);
var write=await http.PostAsync("/api/v1/paper/orders",new StringContent("{}"));Check(write.StatusCode==HttpStatusCode.MethodNotAllowed,"economic writes not exposed");
await Phase4GChecks.Run(http,Check);
var spec=ApiDocument.Create();var served=JsonNode.Parse(await http.GetStringAsync("/openapi/v1.json"));Check(JsonNode.DeepEquals(spec,served),"served OpenAPI equals generated contract");
Directory.CreateDirectory("artifacts/api3100");File.WriteAllText("artifacts/api3100/openapi-v1.json",spec.ToJsonString(new JsonSerializerOptions {WriteIndented=true}));
var baseline=Path.Combine(root,"docs","openapi-v1.json");
if(File.Exists(baseline))Check(ContractCompatibility.Breaks(JsonNode.Parse(File.ReadAllText(baseline))!,spec).Count==0,"OpenAPI backward compatibility");
foreach(var mutation in new[]{"endpoint","required","type","enum"}) {
    var changed=spec.DeepClone();
    if(mutation=="endpoint")changed["paths"]!.AsObject().Remove("/api/v1/system/health");
    if(mutation=="required")changed["components"]!["schemas"]!["HealthDto"]!["required"]!.AsArray().Add("newRequired");
    if(mutation=="type")changed["components"]!["schemas"]!["HealthDto"]!["properties"]!["version"]!["type"]="number";
    if(mutation=="enum")changed["components"]!["schemas"]!["ApiProblem"]!["properties"]!["code"]!["enum"]!.AsArray().RemoveAt(0);
    Check(ContractCompatibility.Breaks(spec,changed).Count>0,"detect breaking "+mutation);
}
await app.StopAsync();await app.DisposeAsync();
await SecurityChecks.Run(Check);
if(args.Contains("--sql"))await SqlIsolation.Run(Check);
Console.WriteLine($"API contract checks: {checks} PASS");

public sealed class TestAuth(IOptionsMonitor<AuthenticationSchemeOptions> o,ILoggerFactory l,UrlEncoder e) : AuthenticationHandler<AuthenticationSchemeOptions>(o,l,e)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()=>Task.FromResult(Request.Headers.TryGetValue("X-Test-User",out var user)?AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier,user.ToString())],Scheme.Name)),Scheme.Name)):AuthenticateResult.NoResult());
}
public sealed class StubReadiness : IApiReadiness {public Task<ReadinessDto> Check(CancellationToken ct)=>Task.FromResult(new ReadinessDto("ready",true,true,true));}
public sealed class StubHistory : IMarketRadarHistoryProvider {public Task<RadarHistory> GetAsync(MarketQuote q,CancellationToken ct)=>Task.FromResult(new RadarHistory([],[],null,null));}
public sealed class StubIndustries : IMarketIndustries {public Task<IndustryDto[]> Get(CancellationToken ct)=>Task.FromResult<IndustryDto[]>([new("2330","Semiconductor")]);}
public sealed class StubRadar : IMarketRadarService
{
    public bool Failure;
    public Task<MarketRadarResult> GetAsync(string market,DateOnly? date,bool analyze,CancellationToken ct) {
        ct.ThrowIfCancellationRequested();if(Failure)throw new HttpRequestException("private upstream error");
        return Task.FromResult(new MarketRadarResult(date??new(2026,9,30),[new(2026,9,30)],market,[new(new("2330","Test",market,false),new(2026,9,30),100.25m,1.25m,1000)],[],[],[],new(2026,9,30,6,0,0,TimeSpan.Zero)));
    }
}
