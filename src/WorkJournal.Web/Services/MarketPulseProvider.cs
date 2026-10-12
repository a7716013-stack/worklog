using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Caching.Memory;
using WorkJournal.Web.Models;
namespace WorkJournal.Web.Services;
public interface IMarketPulseProvider {Task<PulseSnapshot> GetAsync(CancellationToken ct);}
public class MarketPulseProvider(HttpClient http,FinMindStockService stocks,IMemoryCache cache):IMarketPulseProvider
{
 private static readonly SemaphoreSlim Gate=new(1,1);
 public async Task<PulseSnapshot> GetAsync(CancellationToken ct)
 {
  if(cache.TryGetValue<PulseSnapshot>("pulse142",out var found))return found!;
  await Gate.WaitAsync(ct);try{
   if(cache.TryGetValue<PulseSnapshot>("pulse142",out found))return found!;
   var warnings=new List<string>();
   async Task<JsonElement?> Fetch(string url,string label){try{using var response=await http.GetAsync(url,ct);response.EnsureSuccessStatusCode();using var doc=JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));return doc.RootElement.Clone();}catch(Exception e) when(TaiwanMarketRankingProvider.Failure(e,ct)){lock(warnings)warnings.Add(label+"暫時無法取得。");return null;}}
   var listed=Fetch("https://www.twse.com.tw/exchangeReport/MI_INDEX?response=json&type=ALLBUT0999","上市資料");
   var otc=Fetch("https://www.tpex.org.tw/openapi/v1/tpex_mainborad_highlight","上櫃統計");
   var quotes=Fetch("https://www.tpex.org.tw/openapi/v1/tpex_mainboard_daily_close_quotes","上櫃行情");
   SwingStock[] catalog=[];Dictionary<string,string> industries=[];
   try{catalog=await stocks.SwingCatalogAsync(ct);industries=await stocks.TrackingIndustriesAsync(ct);}catch(Exception e) when(TaiwanMarketRankingProvider.Failure(e,ct)){lock(warnings)warnings.Add("分類資料不足，產業與量能統計暫不完整。");}
   await Task.WhenAll(listed,otc,quotes);
   var markets=new List<PulseMarket>();
   if(await listed is {} l && ParseTwse(l,catalog,industries) is {} lm)markets.Add(lm);
   if(await otc is {} o && await quotes is {} q && ParseTpex(o,q,catalog,industries) is {} om)markets.Add(om);
   var today=DateOnly.FromDateTime(DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(8)).DateTime);
   var result=new PulseSnapshot(markets.Where(m=>m.Date<=today).ToArray(),DateTimeOffset.UtcNow,warnings.ToArray());cache.Set("pulse142",result,TimeSpan.FromMinutes(markets.Count==2?5:1));return result;
  }finally{Gate.Release();}
 }
 static string S(JsonElement row,string key)=>row.TryGetProperty(key,out var v)?v.ToString():"";
 static decimal? N(string? value)=>TaiwanMarketRankingProvider.Number(value);
 static int? I(string value)=>int.TryParse(value.Replace(",",""),out var n)?n:null;
 public static PulseMarket? ParseTwse(JsonElement raw,SwingStock[] catalog,Dictionary<string,string> industries)
 {
  var date=TaiwanMarketRankingProvider.Date(S(raw,"date"));if(date==null||!raw.TryGetProperty("tables",out var tables))return null;
  decimal? index=null,change=null;int? up=null,down=null,flat=null,limitUp=null,limitDown=null;var rows=new List<PulseStock>();
  var valid=catalog.Where(x=>x.Market=="twse"&&!x.IsEtf).ToDictionary(x=>x.Symbol);
  foreach(var table in tables.EnumerateArray()){
   if(!table.TryGetProperty("fields",out var fs)||!table.TryGetProperty("data",out var data))continue;var fields=fs.EnumerateArray().Select(x=>x.ToString()).ToArray();
   foreach(var item in data.EnumerateArray()){
    var cells=item.EnumerateArray().Select(x=>x.ToString()).ToArray();string Cell(string key){var n=Array.IndexOf(fields,key);return n>=0&&n<cells.Length?cells[n]:"";}
    if(cells.Length>4&&cells[0]=="發行量加權股價指數"){index=N(cells[1]);change=N(cells[3]);if(Regex.Replace(cells[2],"<[^>]+>","").Trim()=="-")change=-change;}
    if(fields.Contains("股票")){
     var match=Regex.Match(Cell("股票"),@"^(?<all>[\d,]+)(?:\((?<limit>[\d,]+)\))?$");
     if(match.Success){var total=I(match.Groups["all"].Value);var lim=I(match.Groups["limit"].Value);
      if(cells[0].StartsWith("上漲")){up=total;limitUp=lim;}if(cells[0].StartsWith("下跌")){down=total;limitDown=lim;}if(cells[0]=="持平")flat=total;}
    }
    var symbol=Cell("證券代號");if(!valid.TryGetValue(symbol,out var stock)||N(Cell("收盤價")) is not decimal close||close<=0)continue;
    var spread=N(Cell("漲跌價差"));var sign=Regex.Replace(Cell("漲跌(+/-)"),"<[^>]+>","").Trim();if(sign=="-")spread=-spread;else if(sign!="+"&&sign!=""&&sign!=" ")spread=null;
    rows.Add(new(symbol,stock.Name,industries.GetValueOrDefault(symbol,"未分類"),close,close-spread>0?spread/(close-spread)*100:null,N(Cell("最高價")),N(Cell("成交金額"))));
   }
  }
  return new("twse",date.Value,index,change,up,down,flat,limitUp,limitDown,rows.DistinctBy(x=>x.Symbol).ToArray());
 }
 public static PulseMarket? ParseTpex(JsonElement highlights,JsonElement quotes,SwingStock[] catalog,Dictionary<string,string> industries)
 {
  if(highlights.ValueKind!=JsonValueKind.Array||quotes.ValueKind!=JsonValueKind.Array)return null;
  var h=highlights.EnumerateArray().OrderByDescending(x=>TaiwanMarketRankingProvider.Date(S(x,"Date"))).FirstOrDefault();if(h.ValueKind==JsonValueKind.Undefined)return null;
  var date=TaiwanMarketRankingProvider.Date(S(h,"Date"));if(date==null)return null;
  var valid=catalog.Where(x=>x.Market=="tpex"&&!x.IsEtf).ToDictionary(x=>x.Symbol);var rows=new List<PulseStock>();
  foreach(var q in quotes.EnumerateArray()){
   var symbol=S(q,"SecuritiesCompanyCode");if(TaiwanMarketRankingProvider.Date(S(q,"Date"))!=date||!valid.TryGetValue(symbol,out var stock)||N(S(q,"Close")) is not decimal close||close<=0)continue;
   var change=N(S(q,"Change"));rows.Add(new(symbol,stock.Name,industries.GetValueOrDefault(symbol,"未分類"),close,close-change>0?change/(close-change)*100:null,N(S(q,"High")),N(S(q,"TransactionAmount"))));
  }
  return new("tpex",date.Value,N(S(h,"CloseIndex")),N(S(h,"IndexChange")),I(S(h,"PriceRiseCompanyNumbers")),I(S(h,"PriceDeclineCompanyNumbers")),I(S(h,"PriceFlatCompanyNumbers")),I(S(h,"LimitUpCompanyNumbers")),I(S(h,"LimitDownCompanyNumbers")),rows.DistinctBy(x=>x.Symbol).ToArray());
 }
}
