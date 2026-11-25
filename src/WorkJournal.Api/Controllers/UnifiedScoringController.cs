using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using WorkJournal.Application.Scoring;
using WorkJournal.Domain.Scoring;
using WorkJournal.Web.Models;
using WorkJournal.Web.Services;
namespace WorkJournal.Api.Controllers;
[ApiController, Route("api/v1/scoring"), Authorize(Policy="ApiAuthenticatedUser"), EnableRateLimiting("ReadPolicy")]
public sealed class UnifiedScoringController(IUnifiedStockScoringService scoring, FinMindStockService stocks, IConfiguration config) : ControllerBase
{
    [HttpGet("readiness")]
    public ActionResult<ScoreComparison> Readiness(string mode = "Legacy")
    {
        var empty = new VersionedScore(UnifiedStockScoringService.RadarVersion, UnifiedStockScoringService.RadarDigest,
            "LegacyRadar-fixed-v1", null, 0, "Incomplete", [], [], null, null, ["NO_STOCK_SELECTED"], "LEGACY_NOT_CANDIDATE_VALIDATION");
        if (mode == "CandidatePrimary") throw new ApiFault(409, "CANDIDATE_NOT_PROMOTED");
        if (mode is not ("Legacy" or "Shadow" or "Preview")) throw new ApiFault(400, "VALIDATION_ERROR");
        if (mode != "Legacy" && !User.IsInRole("Administrator")) throw new ApiFault(403, "FORBIDDEN");
        return scoring.Compare(empty, mode, User.IsInRole("Administrator"), config.GetValue<bool>("Scoring:ResearchEnabled"));
    }
    [HttpGet("swing")]
    public async Task<ActionResult<SwingAnalysis>> Swing(string symbol, CancellationToken ct)
    {
        ValidateSymbol(symbol);
        try { return scoring.Swing(await stocks.SwingHistoryAsync(symbol, false, ct)); }
        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException or InvalidDataException) { throw new ApiFault(503, "UPSTREAM_UNAVAILABLE"); }
    }
    [HttpGet("backtest/{symbol}")]
    public async Task<ActionResult<BacktestComparison>> Backtest(string symbol, CancellationToken ct)
    {
        ValidateSymbol(symbol);
        // Existing Legacy cache/gateway guards remain authoritative; never launch a Candidate run.
        try { return new BacktestComparison(UnifiedStockScoringService.SwingVersion, await stocks.SwingBacktestAsync(symbol, ct),
            WorkJournal.Web.Services.Evidence.V3CandidateCalculator.Version, "NOT_RUN", "NOT_RUN", "NOT_PROMOTED", ["NO_APPROVED_SIX_COMPONENT_DATASET"]); }
        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException or InvalidDataException) { throw new ApiFault(503, "UPSTREAM_UNAVAILABLE"); }
    }
    private static void ValidateSymbol(string symbol) { if (!System.Text.RegularExpressions.Regex.IsMatch(symbol, @"^[0-9]{4}[0-9A-Z]{0,2}$")) throw new ApiFault(400, "VALIDATION_ERROR"); }
}
