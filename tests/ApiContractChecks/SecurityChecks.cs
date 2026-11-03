using System.Net;
using System.Diagnostics;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Http;
using WorkJournal.Web.Security;
using WorkJournal.Api;
using WorkJournal.Api.Services;
public static class SecurityChecks
{
    public static async Task Run(Action<bool,string> check) {
        var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> { ["Authentication:TrustedProxyKey"]="contract-test-only",["Authentication:Google:PublicOrigin"]="https://trusted.example" }).Build();
        foreach(var supplied in new[]{"forged","contract-test-only"}) {
            var context=new DefaultHttpContext();context.Request.Scheme="http";context.Request.Host=new HostString("untrusted.example");context.Request.Path="/api/v1/system/health";context.Request.Headers["X-WorkJournal-Proxy-Key"]=supplied;
            await new TrustedOriginMiddleware(_=>Task.CompletedTask,config).InvokeAsync(context);
            check(context.Request.Host.Host==(supplied=="forged"?"untrusted.example":"trusted.example"),"API trusted origin validates proxy secret");
            check(!context.Request.Headers.ContainsKey("X-WorkJournal-Proxy-Key"),"proxy secret removed before API transport");
        }
        await using var app=ApiHost.Build(["--environment","Development","--StrategyCenter:Enabled","false","--RadarTracking:SchedulerEnabled","false","--InvestmentInsights:SchedulerEnabled","false",
            "--Api:RateLimits:ReadPolicy:PermitLimit","2","--Api:RateLimits:ReadPolicy:WindowSeconds","60"],b=>{
            b.WebHost.UseTestServer();b.Logging.ClearProviders();b.Services.AddScoped<IApiReadiness,StubReadiness>();
        });
        await app.StartAsync();using var http=app.GetTestClient();
        var unauthorized=await http.GetAsync("/api/v1/paper/orders");
        check(unauthorized.StatusCode==HttpStatusCode.Unauthorized&&unauthorized.Headers.Location is null,"real Identity cookie challenge 401 without redirect");
        var legacy=await http.GetAsync("/StockAnalysis/PaperTrading");
        check(legacy.StatusCode==HttpStatusCode.Redirect&&legacy.Headers.Location!.ToString().Contains("Account/Login"),"legacy cookie login redirect unchanged");
        var one=await http.GetAsync("/api/v1/system/health");var two=await http.GetAsync("/api/v1/system/health");var limited=await http.GetAsync("/api/v1/system/health");
        check(one.IsSuccessStatusCode&&two.IsSuccessStatusCode&&limited.StatusCode==HttpStatusCode.TooManyRequests,"configured fixed-window limiter enforced");
        check((await limited.Content.ReadAsStringAsync()).Contains("RATE_LIMITED"),"rate limit stable problem code");
        var cross=new HttpRequestMessage(HttpMethod.Options,"/api/v1/paper/orders");cross.Headers.Add("Origin","https://untrusted.example");cross.Headers.Add("Access-Control-Request-Method","POST");
        var cors=await http.SendAsync(cross);check(!cors.Headers.Contains("Access-Control-Allow-Origin"),"same-origin CORS boundary");
        await app.StopAsync();
        await using var stalled=ApiHost.Build(["--environment","Development","--ConnectionStrings:JournalDatabase","configured-test-only","--StrategyCenter:Enabled","false","--RadarTracking:SchedulerEnabled","false","--InvestmentInsights:SchedulerEnabled","false"],b=>{
            b.WebHost.UseTestServer();b.Logging.ClearProviders();b.Services.AddScoped<IDatabaseReadiness,StalledDatabase>();
        });
        await stalled.StartAsync();using var probe=stalled.GetTestClient();var elapsed=Stopwatch.StartNew();
        var notReady=await probe.GetAsync("/api/v1/system/readiness");var state=JsonNode.Parse(await notReady.Content.ReadAsStringAsync())!;
        check(notReady.StatusCode==HttpStatusCode.ServiceUnavailable&&!state["databaseReachable"]!.GetValue<bool>(),"stalled SQL gives explicit not-ready");
        check(elapsed.Elapsed<TimeSpan.FromSeconds(12),"readiness dependency wait bounded");
        check((await probe.GetAsync("/api/v1/system/health")).IsSuccessStatusCode,"SQL unavailability does not fail liveness");
        await stalled.StopAsync();
    }
}
public sealed class StalledDatabase : IDatabaseReadiness
{ public async Task<bool> CanConnect(CancellationToken ct) {await Task.Delay(Timeout.InfiniteTimeSpan,ct);return false;} }
