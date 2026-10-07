using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WorkJournal.Web.Controllers;
using WorkJournal.Web.Data;
using WorkJournal.Web.Models;
using WorkJournal.Web.Services;
using WorkJournal.Web.Security;
using WorkJournal.Web.ViewModels;

var database="WorkJournalRadarChecks_"+Guid.NewGuid().ToString("N");
var connection=$@"Server=.\SQLEXPRESS;Database={database};Trusted_Connection=True;Encrypt=True;TrustServerCertificate=True;";
var options=new DbContextOptionsBuilder<JournalDbContext>().UseSqlServer(connection,s=>s.EnableRetryOnFailure()).Options;
await using var setup=new JournalDbContext(options);
var checks=0;void Check(bool ok,string label){if(!ok)throw new Exception("FAIL: "+label);checks++;Console.WriteLine("PASS: "+label);}
var clock=new RadarClock();var radar=new RadarFixture(clock);var source=new TrackingFixture();
MarketRadarRecommendationService Service(JournalDbContext db)=>new(db,radar,source,clock);
async Task<T> Use<T>(Func<JournalDbContext,MarketRadarRecommendationService,Task<T>> action){await using var db=new JournalDbContext(options);return await action(db,Service(db));}
async Task Rejected(Func<Task> action,string label){try{await action();throw new Exception("FAIL: "+label);}catch(ValidationException){Check(true,label);}}
try {
 await setup.Database.MigrateAsync();
 setup.Users.AddRange(new ApplicationUser{Id="alice",UserName="alice"},new ApplicationUser{Id="bob",UserName="bob"});await setup.SaveChangesAsync();
 var day=RadarFixture.Day;var bars=TrackingFixture.Bars(day,25);
 var one=MarketRadarPerformanceCalculator.Evaluate(bars,day,bars[0].Date,100);
 Check(one.Horizons[0].Return==1&&one.Horizons[1].Return==null,"only matured horizon, exact decimal return");
 Check(one.Horizons[0].Mfe==3&&one.Horizons[0].Mae==-2,"MFE/MAE use daily extremes");
 var open=MarketRadarPerformanceCalculator.Evaluate(bars,day,bars[0].Date);
 Check(open.EntryPrice==100&&open.EntryDate==bars[0].Date,"next session open entry");
 var fri=new DateOnly(2026,10,9);var weekend=TrackingFixture.Bars(fri,5);
 Check(weekend[0].Date==new DateOnly(2026,10,12),"fixture skips weekends");
 Check(MarketRadarPerformanceCalculator.Evaluate(weekend,fri,weekend[2].Date,100).Horizons[1].Date==weekend[2].Date,"3D counts bars, not calendar days");
 var bad=bars.ToArray();bad[9]=bad[9] with{Open=180,Close=180,High=182,Low=178};
 var jump=MarketRadarPerformanceCalculator.Evaluate(bad,day,bad[^1].Date,100);
 Check(jump.Horizons[0].Return==1&&jump.Horizons[2].Return==5&&jump.Horizons[3].Return==null,"future corporate jump cannot invalidate earlier horizons");
 Check(MarketRadarPerformanceCalculator.Evaluate(bars,day,day,100).Horizons.All(x=>x.Return==null),"no future leakage");
 Check(MarketRadarPerformanceCalculator.Median([1,3,9,11])==6&&MarketRadarPerformanceCalculator.Median([])==null,"median even and empty");
 Check(MarketRadarStatisticsService.Win([1m,-1m,0m,null])==100m/3&&MarketRadarStatisticsService.Win([null])==null,"win denominator excludes immature, includes flat");
 Check(MarketRadarPerformanceCalculator.Correlation([(1,2),(2,4),(3,6)])==1&&MarketRadarPerformanceCalculator.Correlation([(1,2),(1,4)])==null,"correlation deterministic and zero variance");
 var selection=MarketRadarRecommendationService.SelectDaily(radar.Items);
 Check(selection.Length==10&&selection[0].Score==90&&selection[1].Score==80&&selection[2].Score==null&&selection[^1].Score<70,"top10 category priority and score ordering");
 clock.Now=new(2026,10,5,13,29,0,TimeSpan.FromHours(8));
 Check((await Use((d,s)=>s.CaptureDailyAsync(default))).Captured==0,"before close is no-op");
 clock.Now=clock.Now.AddMinutes(1);radar.Closed=true;
 Check((await Use((d,s)=>s.CaptureDailyAsync(default))).Captured==0&&await setup.MarketRadarCaptureBatches.CountAsync()==0,"closed market cannot create batch");radar.Closed=false;
 radar.Cold=true;Check((await Use((d,s)=>s.CaptureDailyAsync(default))).Captured==0,"missing historical data defers capture");radar.Cold=false;
 var manual=await Use((d,s)=>s.FollowAsync("alice",null,"2330",default));
 await Rejected(()=>Use((d,s)=>s.FollowAsync("bob",manual,null,default)),"private manual recommendation cannot be guessed by another user");
 var captures=await Task.WhenAll(Enumerable.Range(0,4).Select(_=>Use((d,s)=>s.CaptureDailyAsync(default))));
 Check(captures.Sum(x=>x.Captured)==10&&await setup.MarketRadarRecommendations.CountAsync()==10&&await setup.MarketRadarDailySelections.CountAsync()==10,"concurrent daily capture idempotent, reuses manual snapshot");
 Check(await setup.MarketRadarCaptureBatches.CountAsync()==1,"one daily batch");
 var json=await setup.MarketRadarRecommendations.Where(x=>x.Id==manual).Select(x=>x.SnapshotJson).SingleAsync();
 await Use((d,s)=>s.CaptureDailyAsync(default));Check(json==await setup.MarketRadarRecommendations.Where(x=>x.Id==manual).Select(x=>x.SnapshotJson).SingleAsync(),"capture does not rewrite snapshot");
 await using(var guard=new JournalDbContext(options)){var rec=await guard.MarketRadarRecommendations.FirstAsync();rec.Score=0;try{await guard.SaveChangesAsync();throw new Exception("mutable snapshot");}catch(InvalidOperationException){Check(true,"snapshot updates blocked");}}
 await Use((d,s)=>s.FollowAsync("bob",manual,null,default));
 var personal=await setup.MarketRadarPersonalTrackings.AsNoTracking().SingleAsync(x=>x.ApplicationUserId=="alice");
 Check(!await Use((d,s)=>s.StopAsync("bob",personal.Id,default)),"stop enforces owner");
 Check(await Use((d,s)=>s.StopAsync("alice",personal.Id,default)),"stop works");
 clock.Now=clock.Now.AddDays(1);await Use((d,s)=>s.FollowAsync("alice",manual,null,default));
 var rejoined=await setup.MarketRadarPersonalTrackings.AsNoTracking().SingleAsync(x=>x.Id==personal.Id);
 Check(rejoined.JoinedAt==personal.JoinedAt&&rejoined.StoppedAt==null,"rejoin preserves original start");
 clock.Now=new(2026,11,10,18,0,0,TimeSpan.FromHours(8));
 Check(await Use((d,s)=>new MarketRadarPerformanceService(d,source,s,clock,NullLogger<MarketRadarPerformanceService>.Instance).UpdatePendingAsync(default))==10,"performance updates all independent recommendations");
 var perf=await setup.MarketRadarPerformances.AsNoTracking().SingleAsync(x=>x.RecommendationId==manual);
 Check(perf.Return20D==20&&perf.OpenReturn20D==20&&perf.Mfe20D==22&&perf.Mae20D==-2,"20D SQL decimal result and extrema");
 Check(await setup.MarketRadarPersonalTrackings.CountAsync(x=>x.Completed)==2,"both owners mature independently");
 await Use(async(d,s)=>{var stats=new MarketRadarStatisticsService(d,clock);var report=await stats.StatisticsAsync(new(),"alice",default);Check(report.Groups.Sum(x=>x.Count)==10,"SQL grouped dashboard");Check(report.Validation.Any(x=>x.Group=="90–94"&&x.Matured[4]==1),"score bucket matured count");Check(report.Industries.Any(x=>x.Group.Contains("半導體")),"industry classification statistics");var mine=await stats.TrackingAsync(new(){Mine=true},"alice",default);Check(mine.TotalCount==1&&mine.Items[0].Personal?.ApplicationUserId=="alice","SQL personal filter isolated");var etf=await stats.TrackingAsync(new(){Market="etf"},"alice",default);Check(etf.Items.All(x=>x.Recommendation.IsEtf),"ETF filter");return 0;});
 using var memory=new MemoryCache(new MemoryCacheOptions());var market=new FixtureMarket();
 FinMindStockService Stocks()=>new(new HttpClient(market,false){BaseAddress=new Uri("https://fixture.test/")},memory,new ConfigurationBuilder().Build(),NullLogger<FinMindStockService>.Instance);
 await Use(async(d,s)=>{
   var paper=new PaperTradingService(d,Stocks(),Options.Create(new PaperTradingOptions()),new TestUser("alice"));
   var account=await paper.GetAccountAsync(default);var input=new PaperOrderViewModel{StockId="2330",Quantity=10,Side=PaperOrderSide.Buy,OrderType=PaperOrderType.Market,AccountGeneration=account.Generation};
   var order=await paper.PlaceOrderAsync(input,default);var links=new RadarPaperTradingComparisonService(d,s,clock);
   await links.LinkAsync(manual,order.Id,"alice",default);await links.LinkAsync(manual,order.Id,"alice",default);
   Check(await d.MarketRadarPaperTradeLinks.CountAsync()==1,"order link idempotency");
   await Rejected(()=>links.LinkAsync(manual,order.Id,"bob",default),"cannot link someone else's paper order");
   Check((await links.CompareAsync("bob",default)).Count==0,"comparison owner isolation");
   var replay=await paper.PlaceOrderAsync(input,default);Check(replay.Id==order.Id,"linked order retains request idempotency");
   await paper.ResetAccountAsync(account.Generation,default);
   Check(await d.MarketRadarRecommendations.CountAsync()==10&&await d.MarketRadarPaperTradeLinks.CountAsync(x=>x.PaperOrderId==null)==1,"paper reset preserves recommendation and historical association");return 0;
 });
 Console.WriteLine($"MarketRadarTrackingChecks: {checks} PASS");
 if(args.Contains("--serve")) {
   var root=new DirectoryInfo(AppContext.BaseDirectory);while(root!=null&&!File.Exists(Path.Combine(root.FullName,"WorkJournal.sln")))root=root.Parent;
   var web=Path.Combine(root!.FullName,"src","WorkJournal.Web");
   var builder=WebApplication.CreateBuilder(new WebApplicationOptions{ContentRootPath=web,WebRootPath=Path.Combine(web,"wwwroot"),ApplicationName=typeof(StockAnalysisController).Assembly.GetName().Name,EnvironmentName="Development"});
   builder.Configuration["RadarTracking:SchedulerEnabled"]="false";builder.WebHost.UseUrls("http://localhost:5189");builder.Logging.SetMinimumLevel(LogLevel.Warning);
   builder.Services.AddControllersWithViews(o=>o.Filters.Add(new AutoValidateAntiforgeryTokenAttribute())).AddApplicationPart(typeof(StockAnalysisController).Assembly);
   builder.Services.AddDbContext<JournalDbContext>(o=>o.UseSqlServer(connection,s=>s.EnableRetryOnFailure()));builder.Services.AddMemoryCache();builder.Services.AddSingleton<TimeProvider>(clock);builder.Services.AddSingleton<IMarketRadarService>(radar);builder.Services.AddSingleton<IRadarTrackingDataProvider>(source);builder.Services.AddRadarTracking();builder.Services.AddScoped(_=>Stocks());
   builder.Services.Configure<PaperTradingOptions>(_=>{});builder.Services.AddScoped<IPaperTradingService,PaperTradingService>();builder.Services.AddHttpContextAccessor();builder.Services.AddScoped<ICurrentUser,CurrentUser>();builder.Services.AddScoped<StockWatchlistService>();
   builder.Services.AddAuthorization();builder.Services.AddAuthentication("Fixture").AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions,RadarAuthentication>("Fixture",_=>{});
   var app=builder.Build();app.UseStaticFiles();app.UseRouting();app.UseAuthentication();app.UseAuthorization();app.MapControllerRoute("default","{controller=StockAnalysis}/{action=RadarTracking}/{id?}");
   app.MapPost("/__test/stop",async(HttpContext context,Microsoft.AspNetCore.Antiforgery.IAntiforgery af)=>{await af.ValidateRequestAsync(context);app.Lifetime.StopApplication();return Results.Ok();});
   Console.WriteLine("Radar fixture http://localhost:5189");await app.RunAsync();
 }
}
finally{if(database.StartsWith("WorkJournalRadarChecks_")&&database.Length=="WorkJournalRadarChecks_".Length+32)await setup.Database.EnsureDeletedAsync();Console.WriteLine("Isolated radar database removed.");}

public class TestUser(string id):ICurrentUser{public string Id=>id;}
public class RadarClock:TimeProvider{public DateTimeOffset Now=new(2026,10,5,18,0,0,TimeSpan.FromHours(8));public override DateTimeOffset GetUtcNow()=>Now.ToUniversalTime();}
public class TrackingFixture:IRadarTrackingDataProvider
{
 public static SwingBar[] Bars(DateOnly day,int count){var dates=new List<DateOnly>();while(dates.Count<count){day=day.AddDays(1);if(day.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))dates.Add(day);}return dates.Select((d,i)=>new SwingBar(d,100+i,103+i,98+i,101+i,1000)).ToArray();}
 public Task<SwingBar[]> PricesAsync(string symbol,DateOnly from,DateOnly to,CancellationToken ct)=>Task.FromResult(Bars(RadarFixture.Day,30).Where(x=>x.Date>=from&&x.Date<=to).ToArray());
 public Task<RadarResearch> ResearchAsync(MarketRadarStock item,CancellationToken ct)=>Task.FromResult(new RadarResearch(item.Quote.Stock.IsEtf?"ETF":"半導體",3,0,2,0,item.Events));
}
public class RadarFixture(RadarClock clock):IMarketRadarService
{
 public static DateOnly Day=new(2026,10,5);public bool Closed;public bool Cold;
 public MarketRadarStock[] Items=>Enumerable.Range(0,14).Select(i=>new MarketRadarStock{
 Quote=new(new(i==0?"2330":(6000+i).ToString(),i==0?"台積電":"雷達測試"+i,i%2==0?"twse":"tpex",i==2),Day,100,2,10000-i),
 Parts=[new("總分",i is >=2 and <=4?80:100,i==0?90:i==1?80:60-i,"fixture")],ForeignNet=Cold?null:100,Technical=new(){MA20=Cold?null:90},
 Events=[new("2330","<img src=x onerror=alert(1)> 法說會","法說會",clock.Now,"TWSE","javascript:alert(1)","Unknown","<script>alert(1)</script>")]}).ToArray();
 public Task<MarketRadarResult> GetAsync(string market,DateOnly? date,bool analyze,CancellationToken ct){var items=Items;return Task.FromResult(new MarketRadarResult(Closed?Day.AddDays(-1):Day,[Day],market,items.Select(x=>Closed?x.Quote with{Date=Day.AddDays(-1)}:x.Quote).ToArray(),analyze?items:[],[],[],clock.Now));}
}
public class RadarAuthentication(IOptionsMonitor<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions> options,ILoggerFactory logger,System.Text.Encodings.Web.UrlEncoder encoder):Microsoft.AspNetCore.Authentication.AuthenticationHandler<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions>(options,logger,encoder)
{
 protected override Task<Microsoft.AspNetCore.Authentication.AuthenticateResult> HandleAuthenticateAsync(){var user=Request.Headers["X-Test-User"].ToString();if(user=="anonymous")return Task.FromResult(Microsoft.AspNetCore.Authentication.AuthenticateResult.NoResult());if(user!="bob")user="alice";return Task.FromResult(Microsoft.AspNetCore.Authentication.AuthenticateResult.Success(new(new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier,user),new Claim(ClaimTypes.Name,user)],Scheme.Name)),Scheme.Name)));}
}
