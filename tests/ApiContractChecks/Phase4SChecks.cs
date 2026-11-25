using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Security.Cryptography;
using WorkJournal.Application.Scoring;
using WorkJournal.Domain.Research;
using WorkJournal.Web.Models;
using WorkJournal.Web.Services;
using WorkJournal.Web.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
public static class Phase4SChecks
{
    public static async Task Run(HttpClient http, Action<bool,string> check)
    {
        var service=new UnifiedStockScoringService();var day=new DateOnly(2025,1,1);
        var stock=new SwingStock("2330","fixture","twse",false);
        var bars=Enumerable.Range(0,160).Select(i=>new SwingBar(day.AddDays(i),100+i,102+i,99+i,101+i,1000000+i*1000)).ToArray();
        var flows=bars.Select(b=>new SwingFlow(b.Date,10000,2000,1000)).ToArray();
        var history=new SwingHistory(stock,bars,flows,"fixture");
        var legacy=SwingCalculator.Calculate(bars,flows);var unified=service.Swing(history);
        check(JsonSerializer.Serialize(legacy)==JsonSerializer.Serialize(unified.Summary),"4S Swing exact parity including seven strategies");
        check(legacy.Strategies.Count==7,"4S seven strategies retained");
        check(JsonSerializer.Serialize(SwingBacktester.Run(history,day))==JsonSerializer.Serialize(service.Backtest(history,day).Legacy),"4S Legacy backtest exact parity");
        var future=flows.Concat([new SwingFlow(day.AddYears(9),999999,999999,999999)]).ToArray();
        check(JsonSerializer.Serialize(SwingCalculator.Calculate(bars,future))==JsonSerializer.Serialize(legacy),"4S future flows cannot change historical score");
        var quote=new MarketQuote(stock,bars[^1].Date,260,1,1200000);var radarHistory=new RadarHistory(bars,flows,10,day);
        var original=MarketRadarScoreCalculator.Calculate(quote,radarHistory,[]);var adapted=service.Radar(quote,radarHistory,[]);
        check(original.Score==adapted.Score&&original.Earned==adapted.Earned&&original.Available==adapted.Available&&original.Parts.SequenceEqual(adapted.Parts),"4S radar exact legacy parity");
        check(MarketRadarRecommendationService.Category(original)==MarketRadarRecommendationService.Category(adapted),"4S 70 threshold unchanged");
        var saved=JsonSerializer.Serialize(adapted);service.Compare(adapted.Scoring!.Legacy,"Preview",true,true);
        check(saved==JsonSerializer.Serialize(adapted),"4S preview cannot mutate saved snapshot");
        var old=JsonSerializer.Serialize(original);check(JsonSerializer.Deserialize<MarketRadarStock>(old)!.Scoring is null,"4S old snapshot no fabricated comparison");
        check(MarketRadarRecommendationService.SelectDaily([original,adapted]).Length==1,"4S selection dedup/ranking retained");
        var empty=service.Swing(history with { Bars=[],Flows=[] }).Scoring!;
        check(empty.Legacy.TotalScore is null&&empty.Candidate.TotalScore is null&&empty.Candidate.ComponentScores.Length==6&&empty.Candidate.ComponentScores.Sum(c=>c.Weight)==100,"4S missing values stay null with fixed weights");
        foreach(var flag in new[]{false,true})foreach(var mode in new[]{"Shadow","Preview"}) {
            var result=service.Compare(empty.Legacy,mode,true,flag);
            check(result.Candidate.TotalScore is null&&result.Candidate.ResearchStatus=="BLOCKED_BY_DATA"&&result.BaselineStatus=="NOT_RUN"&&result.OosStatus=="NOT_RUN"&&result.PromotionStatus=="NOT_PROMOTED","4S research fail closed "+mode+flag);
        }
        foreach(var mode in new[]{"CandidatePrimary","invalid"}) {
            try {service.Compare(empty.Legacy,mode,true,true);throw new Exception("accepted forbidden mode");}
            catch(Exception ex) when(ex is ArgumentException or InvalidOperationException){check(true,"4S rejects "+mode);}
        }
        check(service.Compare(empty.Legacy).Mode=="Legacy","4S rollback default Legacy");
        check(FormulaCatalog.Identity.FormulaDigest=="81C788B359568B2B058EC92EDFE2F30A3593D7C2A23B3BE8C126BA026D06CBFB","4S candidate formula digest unchanged");
        foreach(var pair in new[]{("MarketRadarScoreCalculator",UnifiedStockScoringService.RadarDigest),("SwingCalculator",UnifiedStockScoringService.SwingDigest)}) {
            var source=File.ReadAllText("src/WorkJournal.Domain/Services/"+pair.Item1+".cs").Replace("\r\n","\n");
            check(Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(source)))==pair.Item2,"4S Legacy formula identity matches source");
        }
        http.DefaultRequestHeaders.Remove("X-Test-User");http.DefaultRequestHeaders.Remove("X-Test-Role");
        foreach(var route in new[]{"readiness","swing?symbol=2330","backtest/2330"})check((await http.GetAsync("/api/v1/scoring/"+route)).StatusCode==HttpStatusCode.Unauthorized,"4S anonymous scoring denied "+route);
        http.DefaultRequestHeaders.Add("X-Test-User","alice");
        check((await http.GetAsync("/api/v1/scoring/readiness?mode=Preview")).StatusCode==HttpStatusCode.Forbidden,"4S member cannot research preview");
        check((await http.GetAsync("/api/v1/scoring/readiness?mode=CandidatePrimary")).StatusCode==HttpStatusCode.Conflict,"4S primary API denied");
        check((await http.GetAsync("/api/v1/scoring/readiness?mode=invalid")).StatusCode==HttpStatusCode.BadRequest,"4S invalid mode fails closed");
        check((await http.GetAsync("/api/v1/scoring/swing?symbol=../bad")).StatusCode==HttpStatusCode.BadRequest,"4S invalid stock blocked before provider");
        http.DefaultRequestHeaders.Add("X-Test-Role","Administrator");
        var response=await http.GetStringAsync("/api/v1/scoring/readiness?mode=Preview");var node=JsonNode.Parse(response)!;
        check(node["candidate"]!["totalScore"] is null&&node["candidate"]!["blockingReasons"]![0]!.GetValue<string>()=="RESEARCH_FEATURE_DISABLED","4S admin cannot bypass disabled feature");
        http.DefaultRequestHeaders.Remove("X-Test-Role");
        var context=new DefaultHttpContext();context.Request.Host=new HostString("localhost");context.Connection.LocalIpAddress=IPAddress.Loopback;context.Connection.RemoteIpAddress=IPAddress.Loopback;
        check(LocalGuestHost.IsLoopback(context),"4S guest loopback admitted");
        context.Request.Host=new HostString("evil.example");check(!LocalGuestHost.IsLoopback(context),"4S guest host spoof denied");
        context.Request.Host=new HostString("localhost");context.Connection.RemoteIpAddress=IPAddress.Parse("192.0.2.1");check(!LocalGuestHost.IsLoopback(context),"4S guest remote IP denied");
        foreach(var environment in new[]{"Production","Development"}) {
            var builder=WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName=environment });
            builder.Configuration["LocalGuest:Enabled"]="true";builder.Configuration["ConnectionStrings:JournalDatabase"]="Server=.\\SQLEXPRESS;Database="+(environment=="Production"?"WorkJournal_LocalGuest_331":"WorkJournal")+";Trusted_Connection=True;TrustServerCertificate=True";
            try{LocalGuestHost.Configure(builder);throw new Exception("guest unsafe config accepted");}catch(InvalidOperationException ex) when(ex.Message.StartsWith("Local guest requires")){check(true,"4S guest unsafe environment/database rejected "+environment);}
        }
        var guestDenied=(await http.PostAsync("/Account/LocalGuest",new StringContent(""))).StatusCode;
        check(guestDenied is HttpStatusCode.BadRequest or HttpStatusCode.NotFound,"4S disabled guest login cannot create session");
    }
}
