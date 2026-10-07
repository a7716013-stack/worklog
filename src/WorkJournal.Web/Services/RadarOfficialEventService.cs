using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using WorkJournal.Web.Models;
namespace WorkJournal.Web.Services;
// Supplementary research only: never passed into the V1 scoring calculator.
public class RadarOfficialEventService(HttpClient client,IMemoryCache cache,ILogger<RadarOfficialEventService> logger)
{
    public async Task<(MarketEvent[] Events,string[] Warnings)> GetAsync(CancellationToken ct)
    {
        const string key="Radar:SupplementaryEvents";
        if(cache.TryGetValue<(MarketEvent[],string[])>(key,out var cached))return cached;
        var events=new List<MarketEvent>();var warnings=new List<string>();
        foreach(var kind in new[]{"notice","punish"})
        {
            var url="https://openapi.twse.com.tw/v1/announcement/"+kind;
            try{using var response=await client.GetAsync(url,ct);response.EnsureSuccessStatusCode();using var doc=JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));events.AddRange(Parse(doc.RootElement,kind,url));}
            catch(Exception e) when(TaiwanMarketRankingProvider.Failure(e,ct)){warnings.Add((kind=="notice"?"上市注意股票":"上市處置資訊")+"暫時無法取得。");logger.LogWarning("Radar extra event source unavailable: {Kind}",kind);}
        }
        var result=(events.DistinctBy(FinMindStockService.EventIdentity).ToArray(),warnings.ToArray());cache.Set(key,result,TimeSpan.FromMinutes(warnings.Count==0?15:1));return result;
    }
    public static MarketEvent[] Parse(JsonElement root,string kind,string url)
    {
        if(root.ValueKind!=JsonValueKind.Array)return [];
        string Text(JsonElement row,string name)=>row.TryGetProperty(name,out var value)?value.ToString().Trim():"";
        return root.EnumerateArray().Where(x=>x.ValueKind==JsonValueKind.Object).Select(x=>{
            var symbol=Text(x,"Code");var date=TaiwanMarketRankingProvider.Date(Text(x,"Date"));
            if(symbol.Length is <4 or >6||date==null)return null;
            var category=kind=="notice"?"注意股票":"處置";var summary=Text(x,kind=="notice"?"TradingInfoForAttention":"Detail");
            // Date-only sources: use end of publication day, never invent an intraday availability time.
            return new MarketEvent(symbol,Text(x,"Name")+" · "+category,category,new(date.Value.ToDateTime(new TimeOnly(23,59,59)),TimeSpan.FromHours(8)),"TWSE",url,"Unknown","來源僅提供公告日期，時間以當日結束保守處理。"+summary);
        }).Where(x=>x!=null).Select(x=>x!).ToArray();
    }
    public static string Category(MarketEvent item)
    {
        var title=item.Title;
        foreach(var pair in new[]{("重大契約","重大契約"),("董事會","董事會重大決議"),("訴訟","訴訟"),("減資","減資"),("增資","增資"),("股利","股利"),("營收","月營收"),("法人說明","法說會"),("法說","法說會"),("處置","處置"),("注意股票","注意股票")})
            if(title.Contains(pair.Item1,StringComparison.Ordinal))return pair.Item2;
        return item.Category;
    }
}
