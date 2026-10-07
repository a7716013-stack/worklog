using Microsoft.Extensions.Caching.Memory;
using WorkJournal.Web.Models;
namespace WorkJournal.Web.Services;
public partial class FinMindStockService
{
    public async Task<Dictionary<string,string>> TrackingIndustriesAsync(CancellationToken ct)
    {
        const string key="radar:tracking:industries";
        if(cache.TryGetValue<Dictionary<string,string>>(key,out var hit))return hit!;
        try {var raw=await ReadAsync("TaiwanStockInfo","",null,null,ct);var result=raw.Where(x=>Text(x,"stock_id")!=null).GroupBy(x=>Text(x,"stock_id")!).ToDictionary(x=>x.Key,x=>Text(x.OrderByDescending(ParseDate).First(),"industry_category")??"未知");cache.Set(key,result,TimeSpan.FromHours(12));return result;}
        catch(Exception e) when(OptionalFailure(e,ct)){return [];}
    }
    public async Task<SwingBar[]> TrackingPricesAsync(string symbol,DateOnly from,DateOnly to,CancellationToken ct)
    {
        var key=$"FinMind:TrackingPrices:{symbol}:{from}:{to}";
        if(cache.TryGetValue<SwingBar[]>(key,out var hit))return hit!;
        var rows=await ReadAsync("TaiwanStockPrice",symbol,from,to,ct);
        var bars=SwingCalculator.Normalize(rows.Where(x=>Text(x,"stock_id")==symbol && ParseDate(x) is { } date && date>=from && date<=to)
            .Select(x=>new SwingBar(ParseDate(x)!.Value,Number(x,"open"),Number(x,"max"),Number(x,"min"),Number(x,"close"),Number(x,"Trading_Volume"))));
        cache.Set(key,bars,TimeSpan.FromMinutes(bars.Length==0?1:15));return bars;
    }
    public async Task<RadarResearch> TrackingResearchAsync(MarketRadarStock item,CancellationToken ct)
    {
        var symbol=item.Quote.Stock.Symbol;var date=item.Quote.Date;var industry=item.Quote.Stock.IsEtf?"ETF":"未知";
        var infoKey="radar:tracking:industries";
        if(!cache.TryGetValue<Dictionary<string,string>>(infoKey,out var info))
        {
            try { var raw=await ReadAsync("TaiwanStockInfo","",null,null,ct); info=raw.Where(x=>Text(x,"stock_id")!=null).GroupBy(x=>Text(x,"stock_id")!).ToDictionary(x=>x.Key,x=>Text(x.OrderByDescending(ParseDate).First(),"industry_category")??"未知");cache.Set(infoKey,info,TimeSpan.FromHours(12)); }
            catch(Exception e) when(OptionalFailure(e,ct)){info=[];cache.Set(infoKey,info,TimeSpan.FromMinutes(1));}
        }
        if(!item.Quote.Stock.IsEtf)industry=info!.GetValueOrDefault(symbol,"未知");
        int? fb=null,fs=null,tb=null,ts=null;decimal? trustNet=null;
        try
        {
            var h=await SwingHistoryAsync(symbol,false,ct);
            var bars=h.Bars.Where(x=>x.Date<=date).ToArray();var flows=h.Flows.Where(x=>x.Date<=date).ToArray();
            if(bars.LastOrDefault()?.Date==date)
            {
                var buy=SwingCalculator.Calculate(bars,flows);
                var sell=SwingCalculator.Calculate(bars,flows.Select(x=>x with{Foreign=-x.Foreign,Trust=-x.Trust}).ToArray());
                fb=buy.ForeignStreak;tb=buy.TrustStreak;fs=sell.ForeignStreak;ts=sell.TrustStreak;
                trustNet=flows.LastOrDefault(x=>x.Date==date)?.Trust;
            }
        }
        catch(Exception e) when(OptionalFailure(e,ct)){ }
        return new(industry,fb,fs,tb,ts,item.Events.GroupBy(x=>EventIdentity(x)).Select(x=>x.First()).ToArray(),trustNet);
    }
    public static string EventIdentity(MarketEvent e)=>Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes($"{e.Source}|{e.StockId}|{e.PublishedAt:O}|{e.Title.Trim()}")));
}
