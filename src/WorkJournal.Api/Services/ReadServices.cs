using System.Reflection;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WorkJournal.Api.Contracts;
using WorkJournal.Web.Data;
using WorkJournal.Web.Services;
using WorkJournal.Web.Services.Evidence;
using WorkJournal.Web.Models;
namespace WorkJournal.Api.Services;
public interface IApiReadiness { Task<ReadinessDto> Check(CancellationToken ct); }
public interface IDatabaseReadiness { Task<bool> CanConnect(CancellationToken ct); }
public sealed class DatabaseReadiness(JournalDbContext db) : IDatabaseReadiness
{ public Task<bool> CanConnect(CancellationToken ct)=>db.Database.CanConnectAsync(ct); }
public sealed class ApiReadiness(IDatabaseReadiness database,IConfiguration configuration,IHostApplicationLifetime lifetime) : IApiReadiness
{
    public async Task<ReadinessDto> Check(CancellationToken ct) {
        var configured=!string.IsNullOrWhiteSpace(configuration.GetConnectionString("JournalDatabase"));
        var reachable=false;
        if(configured) {
            using var deadline=CancellationTokenSource.CreateLinkedTokenSource(ct);
            deadline.CancelAfter(TimeSpan.FromSeconds(5));
            try {reachable=await database.CanConnect(deadline.Token).WaitAsync(deadline.Token);}
            catch(OperationCanceledException) when(ct.IsCancellationRequested){throw;}
            catch { /* A bounded dependency failure is an honest not-ready response, not liveness failure. */ }
        }
        var started=lifetime.ApplicationStarted.IsCancellationRequested;
        return new(started&&configured&&reachable?"ready":"not_ready",started,configured,reachable);
    }
}
public interface IMarketIndustries {Task<IndustryDto[]> Get(CancellationToken ct);}
public sealed class MarketIndustries(FinMindStockService stocks) : IMarketIndustries
{
    public async Task<IndustryDto[]> Get(CancellationToken ct)=>(await stocks.TrackingIndustriesAsync(ct)).OrderBy(x=>x.Key).Select(x=>new IndustryDto(x.Key,x.Value)).ToArray();
}
public static class ReadMapping
{
    public static QuoteDto Quote(MarketQuote x)=>new(new(x.Stock.Symbol,x.Stock.Name,x.Stock.Market,x.Stock.IsEtf),x.Date,x.Close,x.Change,x.ChangePercent,x.Volume);
    public static RadarDto Radar(MarketRadarResult r,PageQuery q)=>new(r.Market,r.TradeDate,r.Dates,r.UpdatedAt,
        PageDto<QuoteDto>.From(r.Quotes.Select(Quote),q),PageDto<RadarAnalysisDto>.From(r.Analysis.Select(x=>new RadarAnalysisDto(Quote(x.Quote),x.Score,x.Earned,x.Available,x.Level,x.Parts.Select(p=>new ScorePartDto(p.Name,p.Maximum,p.Points,p.Explanation)).ToArray(),x.Reasons,x.RiskFlags,
            x.AverageVolume5,x.AverageVolume20,x.VolumeRatio5,x.VolumeRatio20,x.VolumeGrowth20,x.ForeignNet,x.MacdState,
            new(x.Technical.Date,x.Technical.Samples,x.Technical.MA5,x.Technical.MA20,x.Technical.MA60,x.Technical.RSI14,x.Technical.MACD,x.Technical.Signal,x.Technical.Histogram),x.Events.Select(Event).ToArray())),q),PageDto<EventDto>.From(r.Events.Select(Event),q),r.Warnings);
    public static EventDto Event(MarketEvent x)=>new(x.StockId,x.Title,x.Category,x.PublishedAt,x.SourceUrl,x.Impact,x.Summary);
    public static HealthDto Health() {
        var version=typeof(ApiHost).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";
        var parts=version.Split('+',2);return new("ok",parts[0],parts.Length==2?parts[1]:null);
    }
    public static ModelDto Model()=>new(V3CandidateCalculator.Version,"NOT_PROMOTED",
        FullEvidenceModel.Weights.Select(x=>new ComponentDto(x.Key.ToString(),x.Value)).ToArray(),
        null,"unresolved","Evidence strength score. Not probability, expected return, position size, allocation, or trade instruction.");
}
// Reads a sanitized, versioned audit snapshot. Never claims this is a live authority store.
public sealed record ResearchState(ClaimDto[] Claims,CoverageDto Coverage,RunDto[] Runs,ApprovalDto[] Approvals,DateTimeOffset CapturedAt);
public sealed class ResearchReadService
{
    public ResearchState Read() {
        var path=Path.Combine(AppContext.BaseDirectory,"Research","authority-state.json");
        if(!File.Exists(path)) throw new ApiFault(503,"DATA_NOT_READY");
        return JsonSerializer.Deserialize<ResearchState>(File.ReadAllText(path),new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new ApiFault(503,"DATA_NOT_READY");
    }
    public DomainDto[] Domains()=>Read().Claims.GroupBy(x=>x.Domain).Select(g=>new DomainDto(g.Key,g.All(c=>c.Status=="SUPPORTED")?"SUPPORTED":"UNKNOWN",g.Count(),g.Count(c=>c.Status=="SUPPORTED"))).ToArray();
}
