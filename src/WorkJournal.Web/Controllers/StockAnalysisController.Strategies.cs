using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WorkJournal.Web.Data;
using WorkJournal.Web.Models;
using WorkJournal.Web.Services;
using WorkJournal.Web.ViewModels;
namespace WorkJournal.Web.Controllers;
public partial class StockAnalysisController
{
    [HttpGet, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Strategies([FromServices] JournalDbContext db, DateOnly? date, DateOnly? from, string tab = "overview",
        string market = "all", string strategy = "", string symbol = "", string basis = "open", string group = "strategy", int horizon = 20, int? profileId = null, int page = 1, CancellationToken cancellationToken = default)
    {
        var ct = cancellationToken;
        if (symbol.Length > 12 || !new[] { "all", "twse", "tpex" }.Contains(market) || !new[] { 1, 3, 5, 10, 20 }.Contains(horizon)) return BadRequest();
        var vm = new StrategyCenterViewModel { Date = date, From = from, Tab = tab, Market = market, Strategy = strategy, Symbol = symbol, Basis = basis == "close" ? "close" : "open", Group = group, Horizon = horizon, ProfileId = profileId, Page = Math.Max(1, page) };
        vm.Profiles = await db.ScanProfiles.AsNoTracking().OrderBy(x => x.Id).ToListAsync(ct);
        vm.Runs = await db.StrategyScanRuns.AsNoTracking().OrderByDescending(x => x.Id).Take(8).ToListAsync(ct);
        vm.Date ??= await db.StrategyDailyStocks.MaxAsync(x => (DateOnly?)x.TradeDate, ct) ?? vm.Runs.FirstOrDefault()?.TradeDate;
        var query = db.StrategySignals.AsNoTracking().Include(x => x.Snapshot).Include(x => x.Episode).Include(x => x.Performance).AsQueryable();
        if (profileId.HasValue) query = query.Where(x => x.ProfileId == profileId);
        if (market != "all") query = query.Where(x => x.Market == market);
        if (StrategyEngine.Codes.Contains(strategy)) query = query.Where(x => x.StrategyCode == strategy);
        if (symbol.Length > 0) query = query.Where(x => x.Symbol == symbol);
        if (tab == "performance")
        {
            if (from.HasValue) query = query.Where(x => x.TradeDate >= from);
            if (vm.Date.HasValue) query = query.Where(x => x.TradeDate <= vm.Date);
            var entries = await query.Where(x => x.SignalType == "NEW").ToListAsync(ct);
            string Group(StrategySignal s)
            {
                var profile = vm.Profiles.First(x => x.Id == s.ProfileId);
                var prefix = $"{StrategyEngine.Name(s.StrategyCode)} V{s.StrategyVersion} · {profile.Code}/{profile.Version} · {profile.UniverseCode}/{profile.UniverseVersion}";
                return prefix + (group switch { "level" => " · " + StrategyEngine.Level(s.Episode.EntrySignalLevel), "industry" => " · " + (s.Snapshot.Industry.Length == 0 ? "未知產業" : s.Snapshot.Industry), "heat" => " · Heat " + (s.Snapshot.MarketHeat switch { null => "未知", < 30 => "<30", < 45 => "30–44", < 60 => "45–59", < 80 => "60–79", _ => "≥80" }), _ => "" });
            }
            foreach (var g in entries.GroupBy(Group))
            {
                var rows = g.Select(s => s.Performance == null ? null : JsonSerializer.Deserialize<StrategyHorizon[]>(s.Performance.Json)?.FirstOrDefault(x => x.Days == horizon)).Where(x => x != null).ToArray();
                var values = rows.Select(x => vm.Basis == "close" ? x!.CloseReturn : x!.OpenReturn).Where(x => x.HasValue).Select(x => x!.Value).ToArray();
                var mfes = rows.Select(x => vm.Basis == "close" ? x!.CloseMfe : x!.OpenMfe).Where(x => x.HasValue).ToArray();
                var maes = rows.Select(x => vm.Basis == "close" ? x!.CloseMae : x!.OpenMae).Where(x => x.HasValue).ToArray();
                vm.Statistics.Add(new(g.Key, g.Count(), values.Length, values.Length > 0 ? values.Count(x => x > 0) * 100m / values.Length : null,
                    values.Length > 0 ? values.Average() : null, MarketRadarPerformanceCalculator.Median(values), mfes.Length > 0 ? mfes.Average() : null, maes.Length > 0 ? maes.Average() : null));
            }
        }
        else
        {
            if (vm.Date.HasValue) query = query.Where(x => x.TradeDate == vm.Date);
            vm.TotalSignals = await query.CountAsync(ct);
            // Page on symbols so one stock's strategy badges never split across pages.
            var symbols = await query.Select(x => x.Symbol).Distinct().Order().Skip((vm.Page - 1) * 30).Take(30).ToArrayAsync(ct);
            vm.Signals = await query.Where(x => symbols.Contains(x.Symbol)).OrderBy(x => x.Symbol).ThenBy(x => x.StrategyCode).ToListAsync(ct);
        }
        var radar = db.StrategyDailyStocks.AsNoTracking().Where(x => x.TradeDate == vm.Date);
        if (profileId.HasValue) radar = radar.Where(x => x.ProfileId == profileId);
        if (market != "all") radar = radar.Where(x => x.Market == market);
        vm.Radar = await radar.Where(x => x.RadarScore != null).OrderByDescending(x => x.RadarScore).Take(100).ToListAsync(ct);
        vm.Radar = vm.Radar.Where(x => x.RadarScore >= vm.Profiles.First(p => p.Id == x.ProfileId).RadarMinScore).ToList();
        var skips = await db.StrategyEvaluations.AsNoTracking().Where(x => x.TradeDate == vm.Date && x.Status != "Matched").GroupBy(x => x.Reason).Select(g => new { Reason = g.Key, Count = g.Count() }).OrderByDescending(x => x.Count).Take(10).ToListAsync(ct);
        vm.Skips = skips.Select(x => (x.Reason, x.Count)).ToList();
        return View(vm);
    }
    [HttpGet]
    public async Task<IActionResult> StrategySignal([FromServices] JournalDbContext db, long id, CancellationToken cancellationToken)
    {
        var signal = await db.StrategySignals.AsNoTracking().Include(x => x.Snapshot).Include(x => x.Episode).Include(x => x.Performance).SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (signal == null) return NotFound();
        ViewBag.Profile = await db.ScanProfiles.AsNoTracking().SingleAsync(x => x.Id == signal.ProfileId, cancellationToken);
        return View(signal);
    }
}
