using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using WorkJournal.Web.Data;
using WorkJournal.Web.Models;
using WorkJournal.Web.Services;
using WorkJournal.Web.ViewModels;
namespace HomeDashboardChecks;

public static class Checks
{
 static int count;
 static void Check(bool ok,string name){if(!ok)throw new Exception("FAIL: "+name);count++;Console.WriteLine("PASS: "+name);}
 public static async Task Main(string[] args)
 {
  var database="WorkJournalHomeChecks_"+Guid.NewGuid().ToString("N");
  var connection=$@"Server=.\SQLEXPRESS;Database={database};Trusted_Connection=True;Encrypt=True;TrustServerCertificate=True;";
  await using var setup=new JournalDbContext(new DbContextOptionsBuilder<JournalDbContext>().UseSqlServer(connection).Options);
  try {
   await setup.Database.MigrateAsync();
   setup.Users.AddRange(new ApplicationUser{Id="alice",UserName="alice"},new ApplicationUser{Id="bob",UserName="bob"},new ApplicationUser{Id="empty",UserName="empty"});
   await setup.SaveChangesAsync();
   var day=HomeDashboardService.Today;
   setup.MarketRadarCaptureBatches.Add(new(){TradeDate=day,Count=7,CreatedAt=DateTimeOffset.UtcNow});
   setup.WorkLogs.AddRange(new WorkLog{ApplicationUserId="alice",Title="<script>alert(1)</script> 我的今日工作",Content="fixture",WorkDate=day},new WorkLog{ApplicationUserId="bob",Title="BOB_PRIVATE_WORK",Content="fixture",WorkDate=day});
   setup.StockWatchlistItems.Add(new(){ApplicationUserId="alice",Symbol="2330",Name="台積電",Market="twse"});
   for(int i=0;i<3;i++)setup.StockWatchlistItems.Add(new(){ApplicationUserId="bob",Symbol=(6000+i).ToString(),Name="BOB_PRIVATE_STOCK",Market="twse"});
   var account=new PaperTradingAccount{ApplicationUserId="bob",InitialCash=100000,Cash=90000};
   setup.PaperTradingAccounts.Add(account);
   await setup.SaveChangesAsync();
   setup.PaperPositions.Add(new(){AccountId=account.Id,StockId="6000",StockName="BOB_PRIVATE_POSITION",Quantity=10});
   setup.PaperOrders.Add(new(){AccountId=account.Id,StockId="6000",StockName="BOB_PRIVATE_ORDER",Quantity=1,Status=PaperOrderStatus.Pending});
   for(int i=0;i<7;i++){
    var stock=new MarketRadarStock{Quote=new(new((2300+i).ToString(),"推薦 "+i,"twse",false),day,100,2,1000),Parts=[new("完整評分",i==6?80:100,90-i,"fixture")]};
    var r=new MarketRadarRecommendation{TradeDate=day,StockId=stock.Quote.Stock.Symbol,StockName=stock.Quote.Stock.Name,Market="twse",Industry=i==6?"未知":"半導體",Score=stock.Score,SnapshotJson=JsonSerializer.Serialize(stock),CreatedAt=DateTimeOffset.UtcNow};
    setup.MarketRadarDailySelections.Add(new(){TradeDate=day,Rank=i+1,Recommendation=r});
    setup.MarketRadarPersonalTrackings.Add(new(){ApplicationUserId=i==0?"alice":"bob",Recommendation=r,JoinedAt=DateTimeOffset.UtcNow});
   }
   var privateRow=new MarketRadarRecommendation{TradeDate=day,StockId="9999",StockName="BOB_PRIVATE_RADAR",Market="twse",Industry="私人",SnapshotJson="{}",CreatedAt=DateTimeOffset.UtcNow};
   setup.MarketRadarPersonalTrackings.Add(new(){ApplicationUserId="bob",Recommendation=privateRow,JoinedAt=DateTimeOffset.UtcNow});
   await setup.SaveChangesAsync();
   var radar=new RadarFixture();var google=new CalendarFixture();
   await using var factory=new Factory(connection,radar,google);
   using var anonymous=factory.CreateClient(new(){AllowAutoRedirect=false});
   using var alice=factory.CreateClient(new(){AllowAutoRedirect=false});alice.DefaultRequestHeaders.Add("X-Test-User","alice");
   using var bob=factory.CreateClient(new(){AllowAutoRedirect=false});bob.DefaultRequestHeaders.Add("X-Test-User","bob");
   using var empty=factory.CreateClient(new(){AllowAutoRedirect=false});empty.DefaultRequestHeaders.Add("X-Test-User","empty");
   async Task<string> Get(HttpClient client,string path){var r=await client.GetAsync(path);Check(r.StatusCode==HttpStatusCode.OK,path+" HTTP 200");Check(r.Headers.CacheControl?.NoStore==true,path+" no-store");return WebUtility.HtmlDecode(await r.Content.ReadAsStringAsync());}
   var anon=await Get(anonymous,"/");
   Check(anon.Contains("工作有紀錄")&&!anon.Contains("BOB_PRIVATE")&&!anon.Contains("我的今日工作"),"anonymous home contains no personal records");
   Check((await anonymous.GetAsync("/Home/CalendarSummary")).StatusCode==HttpStatusCode.Unauthorized,"anonymous calendar protected");
   var home=await Get(alice,"/");
   Check(home.Contains("我的今日工作")&&!home.Contains("BOB_PRIVATE"),"logged-in own work only");
   var encoded=await alice.GetStringAsync("/");
   Check(!encoded.Contains("<script>alert(1)</script>"),"Razor encodes work title");
   Check(home.Contains("data-bs-target=\"#main-navigation\"")&&home.Contains("aria-expanded=\"false\""),"mobile Bootstrap navigation markup");
   Check(home.Contains("aria-current=\"page\""),"current page accessible");
   foreach(var route in new[]{"/WorkLogs","/StockAnalysis/MarketRadar","/StockAnalysis/Swing#watchlist-heading","/StockAnalysis/RadarTracking","/StockAnalysis/RadarPerformance","/StockAnalysis/PaperTrading","/GoogleCalendar"})Check(home.Contains(route),"navigation route "+route);
   using(var scope=factory.Services.CreateScope()){
    var service=scope.ServiceProvider.GetRequiredService<HomeDashboardService>();
    var a=await service.InvestmentAsync("alice",default);var b=await service.InvestmentAsync("bob",default);
    Check(a.WatchlistCount==1&&b.WatchlistCount==3,"watchlist owner isolation");
    Check(a.TrackingCount==1&&b.TrackingCount==7,"radar tracking owner isolation");
    Check(a.OpenPositionCount==0&&b.OpenPositionCount==1,"position owner isolation");
    Check(a.PendingOrderCount==0&&b.PendingOrderCount==1,"order owner isolation");
    var saved=await service.RadarAsync(default);
    Check(saved?.Count==7&&saved.Top.Count==5,"public top five only");
    Check(saved!.Top[0].Score==90&&saved.Top[0].Level=="強勢追蹤","existing snapshot score/level reused");
    Check(saved.Industries.Single().Count==6,"only known public industries included");
   }
   Check(await setup.PaperTradingAccounts.CountAsync()==1&&await setup.PaperOrders.CountAsync()==1,"homepage reads do not create accounts or execute orders");
   var market=await Get(anonymous,"/Home/MarketSummary");
   Check(market.Contains("推薦 0")&&!market.Contains("BOB_PRIVATE_RADAR")&&!market.Contains("推薦 5"),"public daily selection excludes private recommendations");
   Check(!radar.AnalysisRequested,"homepage never requests score computation");
   var cal=await Get(alice,"/Home/CalendarSummary?userId=bob");
   Check(cal.Contains("alice 行程")&&!cal.Contains("bob 行程"),"calendar ignores supplied owner; claims only");
   Check(!(await alice.GetStringAsync("/Home/CalendarSummary")).Contains("<img src=x onerror"),"calendar Razor encoding");
   google.Fail=true;
   Check((await Get(alice,"/Home/CalendarSummary")).Contains("暫時無法"),"calendar failure fallback");
   await Get(alice,"/");
   radar.Fail=true;
   var failed=await Get(anonymous,"/Home/MarketSummary");
   Check(failed.Contains("市場資料暫時無法取得")&&failed.Contains("推薦 0"),"radar provider failure preserves stored recommendations");
   await Get(alice,"/");
   var emptyHtml=await Get(empty,"/");
   Check(emptyHtml.Contains("今天目前沒有工作項目")&&emptyHtml.Contains("尚未加入自選股"),"empty personal state");
   google.Fail=false;
   Check((await Get(empty,"/Home/CalendarSummary")).Contains("今天目前沒有行程"),"empty calendar state");
   foreach(var path in new[]{"/Home/Index","/WorkLogs","/StockAnalysis","/StockAnalysis/MarketRadar","/StockAnalysis/Swing","/StockAnalysis/RadarTracking","/StockAnalysis/RadarPerformance","/StockAnalysis/PaperTrading","/GoogleCalendar"})
    Check((await anonymous.GetAsync(path)).StatusCode is HttpStatusCode.OK or HttpStatusCode.Redirect or HttpStatusCode.Unauthorized,"existing route resolves "+path);
   radar.Fail=false;
   Console.WriteLine($"HomeDashboardChecks: {count} PASS");
   if(args.Contains("--serve")){
    var builder=WebApplication.CreateBuilder();builder.WebHost.UseUrls("http://localhost:5191");builder.Logging.ClearProviders();
    var app=builder.Build();using var client=factory.CreateClient(new(){AllowAutoRedirect=false});
    app.Run(async context=>{
     if(context.Request.Path=="/__fixture/stop"&&context.Request.Method=="POST"){app.Lifetime.StopApplication();return;}
     using var request=new HttpRequestMessage(new HttpMethod(context.Request.Method),context.Request.Path+context.Request.QueryString);
     var user=context.Request.Headers["X-Test-User"].ToString();
     if(user!="")request.Headers.Add("X-Test-User",user);
     if(context.Request.ContentLength>0){request.Content=new StreamContent(context.Request.Body);request.Content.Headers.TryAddWithoutValidation("Content-Type",context.Request.ContentType);}
     using var response=await client.SendAsync(request);
     context.Response.StatusCode=(int)response.StatusCode;
     foreach(var h in response.Headers.Concat(response.Content.Headers))if(h.Key!="Transfer-Encoding")context.Response.Headers[h.Key]=h.Value.ToArray();
     await response.Content.CopyToAsync(context.Response.Body);
    });
    Console.WriteLine("Home dashboard fixture ready: http://localhost:5191");await app.RunAsync();
   }
  } finally {await setup.Database.EnsureDeletedAsync();}
 }
}
class Factory(string connection,RadarFixture radar,CalendarFixture calendar):WebApplicationFactory<global::Program>
{
 protected override void ConfigureWebHost(IWebHostBuilder builder){
  var root=new DirectoryInfo(AppContext.BaseDirectory);
  while(root!=null&&!File.Exists(Path.Combine(root.FullName,"WorkJournal.sln")))root=root.Parent;
  builder.UseContentRoot(Path.Combine(root!.FullName,"src","WorkJournal.Web"));
  builder.UseEnvironment("Development");builder.UseSetting("RadarTracking:SchedulerEnabled","false");
  builder.ConfigureServices(services=>{
   services.RemoveAll<DbContextOptions<JournalDbContext>>();services.AddDbContext<JournalDbContext>(o=>o.UseSqlServer(connection));
   services.RemoveAll<IMarketRadarService>();services.AddSingleton<IMarketRadarService>(radar);
   services.RemoveAll<IGoogleCalendarService>();services.AddSingleton<IGoogleCalendarService>(calendar);
   services.AddAuthentication(o=>{o.DefaultAuthenticateScheme="DashboardFixture";o.DefaultChallengeScheme="DashboardFixture";}).AddScheme<AuthenticationSchemeOptions,FixtureAuth>("DashboardFixture",_=>{});
   services.AddLogging(x=>x.ClearProviders());
  });
 }
}
class FixtureAuth(IOptionsMonitor<AuthenticationSchemeOptions> options,ILoggerFactory logger,UrlEncoder encoder):AuthenticationHandler<AuthenticationSchemeOptions>(options,logger,encoder)
{
 protected override Task<AuthenticateResult> HandleAuthenticateAsync(){
  var user=Request.Headers["X-Test-User"].ToString();
  return Task.FromResult(user is "alice" or "bob" or "empty" ? AuthenticateResult.Success(new(new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier,user),new Claim(ClaimTypes.Name,user)],Scheme.Name)),Scheme.Name)) : AuthenticateResult.NoResult());
 }
}
class RadarFixture:IMarketRadarService
{
 public bool Fail,AnalysisRequested;
 public Task<MarketRadarResult> GetAsync(string market,DateOnly? date,bool analyze,CancellationToken ct){
  AnalysisRequested|=analyze;if(Fail)throw new HttpRequestException("fixture unavailable");
  var day=HomeDashboardService.Today;
  return Task.FromResult(new MarketRadarResult(day,[day],"all",[new(new("2330","台積電","twse",false),day,100,2,1000)],[],[],[],DateTimeOffset.UtcNow));
 }
}
class CalendarFixture:IGoogleCalendarService
{
 public bool Fail;
 public Task<CalendarConnectionViewModel> GetConnectionAsync(string userId,CancellationToken ct=default){if(Fail)throw new CalendarServiceException("fixture");return Task.FromResult(new CalendarConnectionViewModel(userId!="empty",false,null,true));}
 public Task<IReadOnlyList<CalendarEventViewModel>> GetEventsAsync(string userId,DateTimeOffset start,DateTimeOffset end,CancellationToken ct=default)=>Task.FromResult<IReadOnlyList<CalendarEventViewModel>>([new(){Title=userId+" 行程 <img src=x onerror=alert(1)>",Start=new(HomeDashboardService.Today.ToDateTime(new TimeOnly(9,0)),TimeSpan.FromHours(8)),End=new(HomeDashboardService.Today.ToDateTime(new TimeOnly(10,0)),TimeSpan.FromHours(8)),Source="Google"}]);
 public Task SaveAuthorizationAsync(string u,string a,string e,JsonElement t,CancellationToken ct=default)=>throw new NotSupportedException();
 public Task<CalendarEventViewModel> GetEventAsync(string u,string e,CancellationToken ct=default)=>throw new NotSupportedException();
 public Task CreateEventAsync(string u,CalendarEventInput i,CancellationToken ct=default)=>throw new NotSupportedException();
 public Task UpdateEventAsync(string u,string e,CalendarEventInput i,CancellationToken ct=default)=>throw new NotSupportedException();
 public Task DeleteEventAsync(string u,string e,CancellationToken ct=default)=>throw new NotSupportedException();
 public Task RefreshAccessTokenAsync(string u,CancellationToken ct=default)=>throw new NotSupportedException();
 public Task<bool> DisconnectAsync(string u,CancellationToken ct=default)=>throw new NotSupportedException();
}
