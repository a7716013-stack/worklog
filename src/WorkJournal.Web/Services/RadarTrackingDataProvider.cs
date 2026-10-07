using WorkJournal.Web.Models;
namespace WorkJournal.Web.Services;
public interface IRadarTrackingDataProvider
{
    Task<SwingBar[]> PricesAsync(string symbol,DateOnly from,DateOnly to,CancellationToken ct);
    Task<RadarResearch> ResearchAsync(MarketRadarStock item,CancellationToken ct);
}
public class RadarTrackingDataProvider(FinMindStockService stocks,RadarOfficialEventService extra,TimeProvider clock):IRadarTrackingDataProvider
{
    public Task<SwingBar[]> PricesAsync(string symbol,DateOnly from,DateOnly to,CancellationToken ct)=>stocks.TrackingPricesAsync(symbol,from,to,ct);
    public async Task<RadarResearch> ResearchAsync(MarketRadarStock item,CancellationToken ct)
    {
        var research=await stocks.TrackingResearchAsync(item,ct);var supplemental=await extra.GetAsync(ct);
        var events=research.Events.Concat(supplemental.Events.Where(e=>e.StockId==item.Quote.Stock.Symbol&&DateOnly.FromDateTime(e.PublishedAt.DateTime)<=item.Quote.Date)).Where(e=>e.PublishedAt<=clock.GetUtcNow())
            .Select(e=>e with{Category=RadarOfficialEventService.Category(e),Impact="Unknown"}).DistinctBy(FinMindStockService.EventIdentity).OrderByDescending(e=>e.PublishedAt).ToArray();
        return research with{Events=events,Warnings=supplemental.Warnings};
    }
}
