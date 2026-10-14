using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WorkJournal.Web.Data;
using WorkJournal.Web.Models;
using WorkJournal.Web.Services;

var count = 0;
void Check(bool yes, string name) { if (!yes) throw new Exception("FAIL: " + name); count++; Console.WriteLine("PASS: " + name); }
var sessions = Enumerable.Range(0, 260).Select(i => new DateOnly(2026, 1, 1).AddDays(i)).Where(d => d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday)).ToArray();
var day = sessions[140]; var profile = new ScanProfile(); var universe = new StockUniverseService();
StrategyInput Input(int index)
{
    var date = sessions[index];
    var bars = sessions.Take(index + 1).Select((d,i) => { var c = 100m + i * .2m + (i == index ? 10 : 0); return new StrategyPrice(d, c - .1m, c + 1, c - 1, c, i == index ? 2500000 : 1000000, 50000000); }).ToArray();
    return new(new("2330", "測試普通股", "twse", "半導體", "ESVUFR", true), date, bars,
        sessions.Take(index + 1).Select(d => new SwingFlow(d, 10000, 2000, null)).ToArray(), sessions, 25, date.AddMonths(-1), [], true, 68, DateTimeOffset.UtcNow);
}
var input = Input(140); var f = StrategyEngine.Calculate(input, profile, universe);
using(var expired=JsonDocument.Parse("{\"TradingHaltDate\":\"1150813\",\"TradingResumptionDate\":\"1150814\"}"))
    Check(!StrategyDataProvider.StatusActive(expired.RootElement,new DateOnly(2026,10,14)), "resumed security is not indefinitely excluded");
using(var disposal=JsonDocument.Parse("{\"DispositionPeriod\":\"115/10/13～115/10/19\"}"))
    Check(StrategyDataProvider.StatusActive(disposal.RootElement,new DateOnly(2026,10,14)) && !StrategyDataProvider.StatusActive(disposal.RootElement,new DateOnly(2026,10,20)), "disposal eligibility uses effective interval");
Check(f.Eligibility.Status == "Eligible", "ordinary stock qualifies");
Check(universe.Evaluate(input with { Security = input.Security with { Cfi = "CEOGEU" } }, profile).Status == "NotEligible", "ETF excluded by CFI");
Check(universe.Evaluate(input with { Security = input.Security with { Cfi = "" } }, profile).Status == "NotEligible", "unknown type not inferred from name");
Check(universe.Evaluate(input with { Security = input.Security with { NormalTrading = false } }, profile).Status == "NotEligible", "abnormal excluded");
Check(universe.Evaluate(input with { Prices = input.Prices.TakeLast(119).ToArray() }, profile).Reason == "InsufficientHistory", "120-day minimum");
Check(universe.Evaluate(input with { Prices = input.Prices.Select(x => x with { TradingValue = 19999999 }).ToArray() }, profile).Reason == "LowLiquidity", "liquidity threshold");
Check(universe.Evaluate(input with { Prices = input.Prices.Select(x => x with { TradingValue = 20000000 }).ToArray() }, profile).Status == "Eligible", "liquidity inclusive boundary");
Check(universe.Evaluate(input with { Prices = input.Prices.Select(x => x with { TradingValue = null }).ToArray() }, profile).Status == "InsufficientData", "missing trading value not zero");
Check(f.VolumeMA20 == 1000000 && f.VolumeRatio20 == 2.5m, "volume denominator excludes signal day");
Check(f.Previous20DHigh == input.Prices[^2].High, "previous high excludes signal day");
var future = input.Prices.Append(new(sessions[141], 999, 999, 999, 999, 99999999, 99999999)).ToArray();
Check(StrategyEngine.Calculate(input with { Prices = future }, profile, universe).MA20 == f.MA20, "future bars ignored");
Check(StrategyEngine.Evaluate("TrendBreakout", f, profile).Status == "Matched", "trend breakout all conditions");
Check(StrategyEngine.Evaluate("VolumeBreakout", f, profile).Status == "Matched", "volume breakout independent");
Check(StrategyEngine.Evaluate("InstitutionalMomentum", f, profile).Level == "InstitutionalConfluence", "A level without dealer data");
var b = StrategyEngine.Calculate(input with { Flows = input.Flows.Select(x => x with { Trust = -2000 }).ToArray() }, profile, universe);
Check(StrategyEngine.Evaluate("InstitutionalMomentum", b, profile).Level == "ForeignMomentum", "B level");
var c = StrategyEngine.Calculate(input with { Flows = input.Flows.Select(x => x with { Foreign = -10000 }).ToArray() }, profile, universe);
Check(StrategyEngine.Evaluate("InstitutionalMomentum", c, profile).Level == "TrustMomentum", "C level");
var missing = StrategyEngine.Calculate(input with { Flows = [] }, profile, universe);
Check(StrategyEngine.Evaluate("InstitutionalMomentum", missing, profile).Status == "InsufficientData", "missing flows not false");
Check(StrategyEngine.Evaluate("TrendBreakout", missing, profile).Status == "Matched", "missing flows isolated from technical strategies");
Check(f.RadarScore.HasValue && !StrategyEngine.Calculate(input with { EventCoverageComplete = false }, profile, universe).RadarScore.HasValue, "verified no-event differs from unknown coverage");
f.RadarScore = 75;
Check(StrategyEngine.Evaluate("RadarBenchmark", f, profile).Status == "Matched", "benchmark75 boundary");
f.RadarScore = 74;
Check(StrategyEngine.Evaluate("RadarBenchmark", f, profile).Status == "NotMatched", "benchmark74 not matched");
var after = sessions.Skip(141).Take(20).Select((d,i) => new StrategyPrice(d, 102, 111 + i, 98, 105 + i, 10000, 1000000)).ToArray();
var h = StrategyPerformanceCalculator.Calculate(day, 100, sessions, after, after[4].Date, out var next, out var nextDate);
Check(next == 102 && nextDate == sessions[141], "exact next session open");
Check(h[2].CloseReturn == 9 && h[2].OpenReturn == (109m / 102 - 1) * 100, "same5D endpoint two bases");
Check(h[2].CloseMfe == 15 && h[2].CloseMae == -2, "MFE MAE extremes exclude signal day");
Check(h[3].CloseReturn == null && h[4].OpenReturn == null, "immature null");
var halt = StrategyPerformanceCalculator.Calculate(day, 100, sessions, after.Skip(1), after[4].Date, out var noOpen, out _);
Check(noOpen == null && halt[2].Date == after[4].Date && halt[2].CloseReturn == 9 && halt[2].OpenReturn == null, "halt does not shift endpoint or fallback entry");
Check(halt[2].CloseMfe == null, "missing window cannot fabricate extrema");
var zero = StrategyPerformanceCalculator.Calculate(day, 100, sessions, after.Select(x => x with { Close = 100 }), after[4].Date, out _, out _);
Check(zero[2].CloseReturn == 0, "zero is valid result");
var negativeMfe = StrategyPerformanceCalculator.Calculate(day, 120, sessions, after.Take(1), after[0].Date, out _, out _);
Check(negativeMfe[0].CloseMfe < 0, "raw MFE not clamped");

var dbName = "WorkJournalStrategyChecks_" + Guid.NewGuid().ToString("N");
var connection = $@"Server=.\SQLEXPRESS;Database={dbName};Trusted_Connection=True;Encrypt=True;TrustServerCertificate=True";
var dbOptions = new DbContextOptionsBuilder<JournalDbContext>().UseSqlServer(connection, x => x.EnableRetryOnFailure()).Options;
await using var db = new JournalDbContext(dbOptions);
try
{
    await db.Database.MigrateAsync();
    db.Add(profile); await db.SaveChangesAsync();
    var run = new StrategyScanRun { ProfileId = profile.Id, TradeDate = day }; db.Add(run); await db.SaveChangesAsync();
    var service = new StrategySignalService(db, new StrategySignalSnapshotService(), TimeProvider.System);
    var a = StrategyEngine.Calculate(input, profile, universe);
    var matched = StrategyEngine.Evaluate("InstitutionalMomentum", a, profile);
    await service.SaveAsync(run.Id, profile, a, matched, true, default);
    await service.SaveAsync(run.Id, profile, a, matched, true, default);
    Check(await db.StrategySignals.CountAsync() == 1 && await db.StrategySignalSnapshots.CountAsync() == 1, "atomic snapshot and rerun idempotence");
    Check(await db.StrategySignalPerformances.CountAsync() == 1, "initial performance only");
    var nextFacts = StrategyEngine.Calculate(Input(141), profile, universe);
    await service.SaveAsync(run.Id, profile, nextFacts, matched with { Level = "ForeignMomentum" }, true, default);
    Check(await db.StrategyEpisodes.CountAsync() == 1 && await db.StrategySignals.CountAsync() == 2 && await db.StrategySignalPerformances.CountAsync() == 1, "continue and levelchange sameepisode one sample");
    await service.SaveAsync(run.Id, profile, StrategyEngine.Calculate(Input(142), profile, universe), matched with { Status = "InsufficientData" }, true, default);
    await service.SaveAsync(run.Id, profile, StrategyEngine.Calculate(Input(143), profile, universe), matched, true, default);
    Check(await db.StrategyEpisodes.CountAsync() == 1 && (await db.StrategyEpisodes.SingleAsync()).HasObservationGap, "missing data preserves episode with gap");
    await service.SaveAsync(run.Id, profile, StrategyEngine.Calculate(Input(144), profile, universe), matched with { Status = "NotMatched" }, true, default);
    await service.SaveAsync(run.Id, profile, StrategyEngine.Calculate(Input(145), profile, universe), matched, true, default);
    Check(await db.StrategyEpisodes.CountAsync() == 2 && await db.StrategySignalPerformances.CountAsync() == 2, "notmatched ends and rematch createsnew");
    var snap = await db.StrategySignalSnapshots.FirstAsync(); var original = snap.Json; snap.Json = "bad";
    try { await db.SaveChangesAsync(); throw new Exception("immutable guard missing"); } catch (InvalidOperationException e) when (e.Message.Contains("快照")) { Check(true, "snapshot immutable guard"); }
    db.ChangeTracker.Clear(); Check((await db.StrategySignalSnapshots.FirstAsync()).Json == original, "snapshot unchanged after denied update");
    var profile2 = new ScanProfile { Version = "2", MinAvgTradingValue20D = 30000000 }; db.Add(profile2); await db.SaveChangesAsync();
    var run2 = new StrategyScanRun { ProfileId = profile2.Id, TradeDate = day }; db.Add(run2); await db.SaveChangesAsync();
    await service.SaveAsync(run2.Id, profile2, a, matched, true, default);
    Check(await db.StrategySignals.CountAsync(x => x.TradeDate == day) == 2, "same-day differentprofile allowed");
    var before = await db.StrategySignals.CountAsync();
    try { await new StrategySignalService(db, new FailingSnapshot(), TimeProvider.System).SaveAsync(run2.Id, profile2, nextFacts, matched, true, default); throw new Exception("failure not raised"); }
    catch (InvalidDataException) { Check(true, "snapshot failure propagated"); }
    db.ChangeTracker.Clear();
    Check(await db.StrategySignals.CountAsync() == before && !await db.StrategyEvaluations.AnyAsync(x => x.ProfileId == profile2.Id && x.TradeDate == nextFacts.Input.TradeDate), "failure rolledback evaluation signal episode");
    if (args.Contains("--probe"))
    {
        var services = new ServiceCollection(); services.AddLogging(); services.AddHttpClient("StrategyOfficial"); services.AddMemoryCache();
        using var sp = services.BuildServiceProvider();
        var config = new ConfigurationBuilder().AddUserSecrets("workjournal-google-auth").Build();
        var http = new HttpClient { BaseAddress = new Uri("https://api.finmindtrade.com/api/v4/"), Timeout = TimeSpan.FromSeconds(25) };
        var finmind = new FinMindStockService(http, sp.GetRequiredService<Microsoft.Extensions.Caching.Memory.IMemoryCache>(), config, Microsoft.Extensions.Logging.Abstractions.NullLogger<FinMindStockService>.Instance);
        var ranking = new TaiwanMarketRankingProvider(new HttpClient(), finmind, sp.GetRequiredService<Microsoft.Extensions.Caching.Memory.IMemoryCache>(), Microsoft.Extensions.Logging.Abstractions.NullLogger<TaiwanMarketRankingProvider>.Instance);
        var provider = new StrategyDataProvider(db, finmind, sp.GetRequiredService<IHttpClientFactory>(), ranking, TimeProvider.System, Microsoft.Extensions.Options.Options.Create(new StrategyScanOptions()));
        var actualCalendar = await provider.CalendarAsync(default);
        var stocks = await provider.SecuritiesAsync(default);
        var current = DateOnly.FromDateTime(DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(8)).DateTime);
        var actual = await provider.LoadAsync(stocks.First(x=>x.Symbol=="2330"), actualCalendar.Where(x=>x<current).Max(), actualCalendar, default);
        Console.WriteLine(JsonSerializer.Serialize(new { Probe="official", Securities=stocks.Length, Ordinary=stocks.Count(x=>x.Cfi.StartsWith("ES")), Normal=stocks.Count(x=>x.Cfi.StartsWith("ES")&&x.NormalTrading), Prices=actual.Prices.Length, Flows=actual.Flows.Length, Latest=actual.Prices.LastOrDefault()?.Date, Calls=provider.ApiCalls, Cache=provider.CacheHits }));
    }
    if (args.Contains("--serve"))
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.WebHost.UseUrls("http://localhost:5195");
        builder.Services.AddControllersWithViews().AddApplicationPart(typeof(WorkJournal.Web.Controllers.StockAnalysisController).Assembly);
        builder.Services.AddDbContext<JournalDbContext>(o => o.UseSqlServer(connection));
        builder.Services.AddMemoryCache(); builder.Services.AddHttpClient<FinMindStockService>();
        var app = builder.Build();
        app.UseStaticFiles(new StaticFileOptions { FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../src/WorkJournal.Web/wwwroot"))) });
        app.MapPost("/__stop", () => { app.Lifetime.StopApplication(); return Results.Ok(); });
        app.MapControllerRoute("default", "{controller=StockAnalysis}/{action=Strategies}/{id?}");
        await app.RunAsync();
    }
    Console.WriteLine($"StrategyCenter checks PASS: {count}");
}
finally { await db.Database.EnsureDeletedAsync(); }
class FailingSnapshot : IStrategySignalSnapshotService { public StrategySignalSnapshot Create(StrategyFacts f) => throw new InvalidDataException("fixture rollback"); }
