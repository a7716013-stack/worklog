using Microsoft.Extensions.Caching.Memory;
using WorkJournal.Web.Models;
namespace WorkJournal.Web.Services;

public interface IMarketRadarHistoryProvider { Task<RadarHistory> GetAsync(MarketQuote quote,CancellationToken ct); }
public class FinMindRadarHistoryProvider(FinMindStockService stocks) : IMarketRadarHistoryProvider
{
    public Task<RadarHistory> GetAsync(MarketQuote quote,CancellationToken ct) => stocks.RadarHistoryAsync(quote.Stock.Symbol,quote.Date,quote.Stock.IsEtf,ct);
}
public interface IMarketRadarService
{
    Task<MarketRadarResult> GetAsync(string market,DateOnly? date,bool analyze,CancellationToken ct);
}
public class MarketRadarService(ITaiwanMarketRankingProvider provider, IMarketRadarHistoryProvider history,
    IMemoryCache cache, ILogger<MarketRadarService> logger) : IMarketRadarService
{
    // Across all requests, at most four stocks may load histories concurrently.
    private static readonly SemaphoreSlim HistoryGate=new(4,4);
    private static readonly SemaphoreSlim AnalysisGate=new(1,1);
    public static MarketQuote[] Candidates(IEnumerable<MarketQuote> rows)
    {
        var all=rows.ToArray();
        return all.Where(x=>x.ChangePercent>0).OrderByDescending(x=>x.ChangePercent).ThenBy(x=>x.Stock.Symbol).Take(20)
            .Concat(all.Where(x=>x.ChangePercent<0).OrderBy(x=>x.ChangePercent).ThenBy(x=>x.Stock.Symbol).Take(20))
            .Concat(all.OrderByDescending(x=>x.Volume).ThenBy(x=>x.Stock.Symbol).Take(20)).DistinctBy(x=>x.Stock.Symbol).ToArray();
    }
    public async Task<MarketRadarResult> GetAsync(string market,DateOnly? date,bool analyze,CancellationToken ct)
    {
        var snapshot=await provider.GetAsync(ct);
        var dates=snapshot.Quotes.Select(x=>x.Date).Distinct().OrderDescending().ToArray();
        var selected=date ?? dates.Cast<DateOnly?>().FirstOrDefault();
        bool Matches(SwingStock s)=>market switch {"twse"=>s.Market=="twse"&&!s.IsEtf,"tpex"=>s.Market=="tpex"&&!s.IsEtf,"etf"=>s.IsEtf,_=>true};
        var rows=snapshot.Quotes.Where(x=>x.Date==selected && Matches(x.Stock)).ToArray();
        var symbols=snapshot.Quotes.Where(x=>Matches(x.Stock)).Select(x=>x.Stock.Symbol).ToHashSet();
        // Announcements from suspended stocks must remain visible even without a valid closing quote.
        var events=snapshot.Events.Where(x=>(market=="all" || market=="twse" && x.Source=="TWSE" || market=="tpex" && x.Source=="TPEx" || market=="etf" && symbols.Contains(x.StockId))
            && selected.HasValue && DateOnly.FromDateTime(x.PublishedAt.Date)<=selected).ToArray();
        var warnings=snapshot.Warnings.ToList();
        if(dates.Length>1) warnings.Add("來源交易日不同，僅排列所選日期；可切換日期查看其他市場資料。");
        if(rows.Length==0) warnings.Add("所選日期及市場沒有可用行情。");
        if(events.Length==0) warnings.Add("重大資訊來源目前未提供所選範圍資料；不代表沒有重大消息。");
        if(!analyze || rows.Length==0) return new(selected,dates,market,rows,[],events,warnings.ToArray(),snapshot.UpdatedAt);
        var candidates=Candidates(rows);
        MarketRadarStock[] analysis;
        // Prevent concurrent page loads from repeating the same expensive candidate batch.
        await AnalysisGate.WaitAsync(ct);
        try
        {
            analysis=await Task.WhenAll(candidates.Select(async quote=>
            {
                var key=$"radar:FinMind:history:{quote.Date:yyyyMMdd}:{quote.Stock.Symbol}";
                await HistoryGate.WaitAsync(ct);
                try
                {
                    if(!cache.TryGetValue<RadarHistory>(key,out var data))
                    {
                        try { data=await history.GetAsync(quote,ct); cache.Set(key,data,TimeSpan.FromMinutes(data.Bars.Length==0 || data.Flows.Length==0 ? 1 : 15)); }
                        catch(Exception e) when(TaiwanMarketRankingProvider.Failure(e,ct))
                        { logger.LogWarning("Radar history failed for {Symbol} ({Type})",quote.Stock.Symbol,e.GetType().Name); data=new([],[],null,null); cache.Set(key,data,TimeSpan.FromMinutes(1)); }
                    }
                    return MarketRadarScoreCalculator.Calculate(quote,data!,events);
                }
                finally { HistoryGate.Release(); }
            }));
        }
        finally { AnalysisGate.Release(); }
        if(analysis.Any(x=>x.Score is null)) warnings.Add("部分候選資料不足；顯示已知得分與可評權重，不換算或冒充完整 100 分。");
        return new(selected,dates,market,rows,analysis,events,warnings.Distinct().ToArray(),snapshot.UpdatedAt);
    }
}
