using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using WorkJournal.Web.Controllers;
using WorkJournal.Web.Data;
using WorkJournal.Web.Models;
using WorkJournal.Web.Security;
using WorkJournal.Web.Services;

var count=0;
void Check(bool ok,string label){if(!ok)throw new Exception(label);Console.WriteLine("PASS "+label);count++;}
var clock=new TestClock();var pulse=new TestPulse();var radar=new TestRadar();var date=TestPulse.Day;
PaperTrade Trade(string symbol,int qty,decimal cash,int order)=>new(){Id=order,StockId=symbol,Quantity=qty,Side=cash<0?PaperOrderSide.Buy:PaperOrderSide.Sell,NetCashAmount=cash,ExecutedAt=clock.Now.AddMinutes(order)};
PaperTrade[] trades=[Trade("2330",10,-1020,1),Trade("2330",10,-1020,2),Trade("2330",10,1180,3),Trade("2330",10,980,4),Trade("LOSS",10,-1020,5),Trade("LOSS",10,880,6),Trade("FLAT",10,-1000,7),Trade("FLAT",10,1000,8),Trade("OPEN",10,-1000,9)];
var stats=PortfolioAnalyticsCalculator.Calculate(trades);
Check(stats.Completed==3&&stats.Wins==1&&stats.Losses==1&&stats.Flat==1,"closed cycles exclude open positions");
Check(stats.Investments.Single(x=>x.Symbol=="2330").Profit==120,"partial exits and buy costs form one investment");
Check(PortfolioAnalyticsCalculator.Calculate(trades.Take(3)).Completed==0,"partial exit not completed trade");
Check(stats.WinRate==100m/3&&stats.ProfitFactor==120m/140,"win rate includes flat; PF uses money");
Check(stats.AverageWin==120m/2040*100&&stats.AverageLoss==-140m/1020*100,"average returns include costs");
Check(stats.PayoffRatio==stats.AverageWin/-stats.AverageLoss,"payoff ratio uses returns");
Check(PortfolioAnalyticsCalculator.Calculate(trades.Take(4)).ProfitFactor==null,"no loss PF unavailable");
Check(PortfolioAnalyticsCalculator.Calculate([]).WinRate==null,"empty statistics unavailable");
var samples=new[]{1000000m,1300000m,1190540m,1400000m}.Select((v,i)=>new PortfolioDailyValue{TradeDate=date.AddDays(i-4),TotalAssets=v}).ToArray();
Check(PortfolioAnalyticsCalculator.Drawdown(samples)==-8.42m,"maximum drawdown measures peak to subsequent trough");
Check(PortfolioAnalyticsCalculator.Drawdown(samples.Take(1))==null,"one sample cannot measure drawdown");
var current=pulse.Data.Markets[0];
var past=Enumerable.Range(1,20).Select(i=>current with{Date=date.AddDays(-i),Stocks=[new("2330","台積電","半導體",100,0,105,1000000)]}).ToArray();
var heat=MarketHeatCalculator.Calculate(current,past);
Check(heat.Score==91&&heat.HistoryDays==20,"market heat weighted complete score");
Check(MarketHeatCalculator.Calculate(current,past.Take(19).ToArray()).Score==null,"insufficient history is not normalized to 100");
Check(MarketHeatCalculator.Calculate(current,past.Concat(past).ToArray()).Score==heat.Score,"duplicate days do not expand history");
Check(MarketHeatCalculator.Calculate(current,past.Select(x=>x with{Date=date.AddDays(1)}).ToArray()).Score==null,"future observations excluded");
Check(MarketHeatCalculator.Calculate(current with{Stocks=[..current.Stocks,new("OTHER","Other","Other",100,1,101,1000)]},past).Score==null,"insufficient stock coverage suppresses full score");
Check(MarketHeatCalculator.Calculate(current,past.Select(x=>x with{Stocks=[x.Stocks[0] with{Amount=null}]}).ToArray()).Score==null,"missing turnover not treated as zero");
using(var raw=JsonDocument.Parse("""{"date":"20261012","tables":[{"fields":["a","b","c","d","e"],"data":[["發行量加權股價指數","23,000","<span>-</span>","100","0"]]},{"fields":["類型","股票"],"data":[["上漲(漲停)","623(18)"],["下跌(跌停)","318(2)"],["持平","87"]]},{"fields":["證券代號","收盤價","漲跌價差","漲跌(+/-)","最高價","成交金額"],"data":[["2330","110","10","+","112","1,000,000"],["0050","50","1","+","51","999"]]}]}""")){
 var parsed=MarketPulseProvider.ParseTwse(raw.RootElement,[new("2330","台積電","twse",false),new("0050","ETF","twse",true)],new(){{"2330","半導體"}})!;
 Check(parsed.IndexChange==-100&&parsed.Up==623&&parsed.LimitUp==18&&parsed.Flat==87,"official TWSE signed index and breadth parser");
 Check(parsed.Stocks.Length==1&&parsed.Turnover==1000000&&parsed.Stocks[0].ChangePercent==10,"TWSE excludes ETF, amounts in TWD, prior-close return");
}
using(var h=JsonDocument.Parse("""[{"Date":"1151012","CloseIndex":"300","IndexChange":"-3","PriceRiseCompanyNumbers":"356","PriceDeclineCompanyNumbers":"416","PriceFlatCompanyNumbers":"95","LimitUpCompanyNumbers":"23","LimitDownCompanyNumbers":"3","DailyTradingValue":"999999"}]"""))
using(var q=JsonDocument.Parse("""[{"Date":"1151012","SecuritiesCompanyCode":"6488","Close":"90","Change":"-10","High":"95","TransactionAmount":"123456"},{"Date":"1151008","SecuritiesCompanyCode":"OLD","Close":"10"}]""")){
 var parsed=MarketPulseProvider.ParseTpex(h.RootElement,q.RootElement,[new("6488","環球晶","tpex",false),new("OLD","Old","tpex",false)],[])!;
 Check(parsed.Date==date&&parsed.Stocks.Length==1&&parsed.Stocks[0].ChangePercent==-10,"TPEx ROC date, stale rows and signed change");
 Check(parsed.Turnover==123456&&parsed.LimitDown==3,"TPEx turnover uses unambiguous per-stock amounts");
}
var database="WorkJournalInsightsChecks_"+Guid.NewGuid().ToString("N");
var connection=$@"Server=.\SQLEXPRESS;Database={database};Trusted_Connection=True;Encrypt=True;TrustServerCertificate=True;";
var options=new DbContextOptionsBuilder<JournalDbContext>().UseSqlServer(connection,sql=>sql.EnableRetryOnFailure()).Options;
await using var db=new JournalDbContext(options);
try{
 await db.Database.MigrateAsync();
 db.Users.AddRange(new ApplicationUser{Id="alice",UserName="alice"},new ApplicationUser{Id="bob",UserName="bob"});
 var alice=new PaperTradingAccount{ApplicationUserId="alice",InitialCash=1000000,Cash=999000};var bob=new PaperTradingAccount{ApplicationUserId="bob",InitialCash=500000,Cash=500000};
 db.AddRange(alice,bob);await db.SaveChangesAsync();
 db.PaperPositions.Add(new(){AccountId=alice.Id,StockId="2330",StockName="台積電",Quantity=10,CostBasis=1000,AverageCost=100});await db.SaveChangesAsync();
 var service=new PortfolioAnalyticsService(db,radar,clock);
 clock.Now=new(2026,10,12,13,39,0,TimeSpan.FromHours(8));await service.CaptureAsync(alice.Id,default);Check(!await db.PortfolioDailyValues.AnyAsync(),"before close does not capture");
 clock.Now=new(2026,10,11,14,30,0,TimeSpan.FromHours(8));await service.CaptureAsync(alice.Id,default);Check(!await db.PortfolioDailyValues.AnyAsync(),"weekend does not capture");
 clock.Now=new(2026,10,12,14,30,0,TimeSpan.FromHours(8));radar.Mode="holiday";await service.CaptureAsync(alice.Id,default);Check(!await db.PortfolioDailyValues.AnyAsync(),"no actual trading session does not capture");
 radar.Mode="missing";await service.CaptureAsync(alice.Id,default);Check(!await db.PortfolioDailyValues.AnyAsync(),"missing held quote does not invent valuation");
 radar.Mode="valid";await service.CaptureAsync(alice.Id,default);await service.CaptureAsync(alice.Id,default);
 var point=await db.PortfolioDailyValues.SingleAsync();Check(point.TotalAssets==1000100&&point.MarketValue==1100,"capture cash plus complete closing valuation; idempotent");
 JsonElement Json(object o)=>JsonSerializer.SerializeToElement(o,new JsonSerializerOptions(JsonSerializerDefaults.Web));
 Check(Json(await service.HistoryAsync("bob",default)).GetProperty("points").GetArrayLength()==0,"history isolated by signed-in owner");
 Check(Json(await service.HistoryAsync("alice",default)).GetProperty("points").GetArrayLength()==1,"owner sees only own history");
 var tracked=await db.PaperTradingAccounts.SingleAsync(x=>x.Id==alice.Id);tracked.Generation=Guid.NewGuid();await db.SaveChangesAsync();
 Check(Json(await service.HistoryAsync("alice",default)).GetProperty("points").GetArrayLength()==0,"account reset generation hides old curve");
 await service.CaptureAsync(alice.Id,default);Check(await db.PortfolioDailyValues.CountAsync()==2,"new generation capture does not overwrite old generation");
 var markets=new MarketPulseService(db,pulse,clock);await markets.CaptureAsync(default);await markets.CaptureAsync(default);Check(await db.MarketPulseDays.CountAsync()==2,"market snapshots upsert each date and market");
 var overview=Json(await markets.OverviewAsync("all",default));Check(overview.GetProperty("complete").GetBoolean()&&overview.GetProperty("summary").GetProperty("up").GetInt32()==140,"same day aggregate combines market breadth");
 var saved=pulse.Data;pulse.Data=saved with{Markets=[saved.Markets[0],saved.Markets[1] with{Date=date.AddDays(-1)}]};
 Check(Json(await markets.OverviewAsync("all",default)).GetProperty("summary").ValueKind==JsonValueKind.Null,"different market dates never combined");pulse.Data=saved;
 foreach(var row in past)db.MarketPulseDays.Add(new(){Market="twse",TradeDate=row.Date,Json=JsonSerializer.Serialize(row)});
 foreach(var row in past){if(await db.MarketPulseDays.AnyAsync(x=>x.Market=="tpex"&&x.TradeDate==row.Date))continue;db.MarketPulseDays.Add(new(){Market="tpex",TradeDate=row.Date,Json=JsonSerializer.Serialize(row with{Market="tpex",Stocks=[new("6488","環球晶","半導體",100,0,105,1000000)]})});}
 foreach(var row in samples){row.AccountId=alice.Id;row.Generation=tracked.Generation;row.InitialCash=1000000;row.Cash=999000;row.MarketValue=row.TotalAssets-row.Cash;db.Add(row);}await db.SaveChangesAsync();
 Console.WriteLine($"Investment insights: {count} assertions passed.");
 if(args.Contains("--serve")){
  var root=Path.GetFullPath("src/WorkJournal.Web");var builder=WebApplication.CreateBuilder(new WebApplicationOptions{ContentRootPath=root,WebRootPath=Path.Combine(root,"wwwroot"),ApplicationName=typeof(StockAnalysisController).Assembly.GetName().Name,EnvironmentName="Development"});
  builder.WebHost.UseUrls("http://localhost:5186");builder.Logging.SetMinimumLevel(LogLevel.Warning);
  builder.Services.AddControllersWithViews(o=>o.Filters.Add(new AutoValidateAntiforgeryTokenAttribute())).AddApplicationPart(typeof(StockAnalysisController).Assembly);
  builder.Services.AddDbContext<JournalDbContext>(o=>o.UseSqlServer(connection,sql=>sql.EnableRetryOnFailure()));builder.Services.AddMemoryCache();builder.Services.AddHttpContextAccessor();
  builder.Services.AddScoped<ICurrentUser,CurrentUser>();builder.Services.AddScoped<StockWatchlistService>();builder.Services.AddScoped<StockFavoriteService>();
  builder.Services.AddSingleton<TimeProvider>(clock);builder.Services.AddSingleton<IMarketPulseProvider>(pulse);builder.Services.AddSingleton<IMarketRadarService>(radar);
  builder.Services.AddScoped<MarketPulseService>();builder.Services.AddScoped<PortfolioAnalyticsService>();builder.Services.Configure<PaperTradingOptions>(_=>{});builder.Services.AddScoped<IPaperTradingService,PaperTradingService>();
  var fixture=new FixtureMarket{Date=date};builder.Services.AddScoped(sp=>new FinMindStockService(new HttpClient(fixture,false){BaseAddress=new("https://fixture.test/")},sp.GetRequiredService<IMemoryCache>(),new ConfigurationBuilder().Build(),NullLogger<FinMindStockService>.Instance));
  builder.Services.AddAuthentication("Fixture").AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions,TestAuth>("Fixture",_=>{});builder.Services.AddAuthorization();
  var app=builder.Build();app.UseStaticFiles();app.UseRouting();app.UseAuthentication();app.UseAuthorization();app.MapControllerRoute("default","{controller=WorkLogs}/{action=Index}/{id?}");
  app.MapPost("/__test/stop",async(HttpContext c,Microsoft.AspNetCore.Antiforgery.IAntiforgery a)=>{await a.ValidateRequestAsync(c);app.Lifetime.StopApplication();return Results.Ok();});
  Console.WriteLine("Insights fixture ready: http://localhost:5186");await app.RunAsync();
 }
}finally{if(database.StartsWith("WorkJournalInsightsChecks_")&&database.Length=="WorkJournalInsightsChecks_".Length+32)await db.Database.EnsureDeletedAsync();Console.WriteLine("Isolated database removed.");}
public class TestClock:TimeProvider{public DateTimeOffset Now=new(2026,10,12,14,30,0,TimeSpan.FromHours(8));public override DateTimeOffset GetUtcNow()=>Now.ToUniversalTime();}
public class TestPulse:IMarketPulseProvider{
 public static readonly DateOnly Day=new(2026,10,12);
 public PulseSnapshot Data=new([new("twse",Day,23000,100,70,20,10,18,2,[new("2330","台積電","半導體",110,10,112,2000000)]),new("tpex",Day,300,-3,70,20,10,4,1,[new("6488","環球晶","半導體",108,8,110,2000000)])],DateTimeOffset.UtcNow,[]);
 public Task<PulseSnapshot> GetAsync(CancellationToken ct)=>Task.FromResult(Data);
}
public class TestRadar:IMarketRadarService{
 public string Mode="valid";
 public Task<MarketRadarResult> GetAsync(string market,DateOnly? date,bool analyze,CancellationToken ct){MarketQuote[] quotes=Mode=="holiday"?[]:[new(new(Mode=="missing"?"OTHER":"2330","台積電","twse",false),TestPulse.Day,110,10,1000),new(new("6488","環球晶","tpex",false),TestPulse.Day,108,8,1000)];return Task.FromResult(new MarketRadarResult(TestPulse.Day,[TestPulse.Day],market,quotes,[],[],[],DateTimeOffset.UtcNow));}
}
public class TestAuth(Microsoft.Extensions.Options.IOptionsMonitor<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions> o,ILoggerFactory l,System.Text.Encodings.Web.UrlEncoder e):Microsoft.AspNetCore.Authentication.AuthenticationHandler<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions>(o,l,e){
 protected override Task<Microsoft.AspNetCore.Authentication.AuthenticateResult> HandleAuthenticateAsync(){var id=Request.Headers["X-Test-User"].FirstOrDefault()??"alice";return Task.FromResult(id=="anonymous"?Microsoft.AspNetCore.Authentication.AuthenticateResult.NoResult():Microsoft.AspNetCore.Authentication.AuthenticateResult.Success(new(new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier,id),new Claim(ClaimTypes.Name,id)],Scheme.Name)),Scheme.Name)));}
}
