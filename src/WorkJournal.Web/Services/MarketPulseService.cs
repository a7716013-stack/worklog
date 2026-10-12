using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WorkJournal.Web.Data;
using WorkJournal.Web.Models;
namespace WorkJournal.Web.Services;
public record HeatPart(string Name,decimal Maximum,decimal? Points,string Basis);
public record MarketHeat(decimal? Score,string Label,HeatPart[] Parts,int HistoryDays,int EligibleStocks);
public static class MarketHeatCalculator
{
 public static MarketHeat Calculate(PulseMarket current,IReadOnlyList<PulseMarket> history)
 {
  var previous=history.Where(x=>x.Date<current.Date).GroupBy(x=>x.Date).Select(g=>g.Last()).OrderByDescending(x=>x.Date).Take(20).ToArray();
  var total=current.Up+current.Down+current.Flat;decimal? breadth=total>0?current.Up*30m/total:null;
  decimal? volume=null;if(previous.Length==20&&previous.All(x=>x.Turnover>0)&&current.Turnover>=0)volume=Math.Clamp(current.Turnover!.Value/previous.Average(x=>x.Turnover)!.Value/2,0,1)*25;
  int eligible=0,above=0,breakouts=0;var maps=previous.Select(x=>x.Stocks.ToDictionary(s=>s.Symbol)).ToArray();
  foreach(var stock in current.Stocks){var past=maps.Select(m=>m.GetValueOrDefault(stock.Symbol)).ToArray();if(past.Length!=20||past.Any(x=>x==null||x.High==null))continue;eligible++;if(stock.Close>(past.Take(19).Sum(x=>x!.Close)+stock.Close)/20)above++;if(stock.Close>past.Max(x=>x!.High))breakouts++;}
  var coverage=current.Stocks.Length>0?(decimal)eligible/current.Stocks.Length:0;
  decimal? trend=eligible>0&&coverage>=.8m?above*30m/eligible:null;
  decimal? strength=eligible>0&&coverage>=.8m?Math.Clamp(breakouts/(decimal)eligible/.2m,0,1)*15:null;
  HeatPart[] parts=[new("漲跌廣度",30,breadth,"上漲／（上漲＋下跌＋平盤）×30"),new("市場量能",25,volume,"今日一般股票成交額／前20筆觀測均額，2倍滿分25"),new("趨勢廣度",30,trend,"站上含今日20筆觀測均價的有效樣本比例×30；歷史覆蓋至少80%"),new("突破比例",15,strength,"突破前20筆觀測最高價的比例，20%滿分15；覆蓋至少80%")];
  decimal? score=parts.All(x=>x.Points.HasValue)?decimal.Round(parts.Sum(x=>x.Points!.Value),0):null;
  return new(score,score==null?"暫無完整評分":score>=80?"非常強勢":score>=60?"強勢":score>=45?"中性":score>=30?"偏弱":"弱勢",parts,previous.Length,eligible);
 }
}
public class MarketPulseService(JournalDbContext db,IMarketPulseProvider source,TimeProvider clock)
{
 public async Task<PulseSnapshot> CaptureAsync(CancellationToken ct)
 {
  var data=await source.GetAsync(ct);var now=clock.GetUtcNow().ToOffset(TimeSpan.FromHours(8));var today=DateOnly.FromDateTime(now.DateTime);
  var valid=data.Markets.Where(m=>m.Stocks.Length>0&&m.Date<=today&&(m.Date<today||now.TimeOfDay>=new TimeSpan(13,40,0))).ToArray();
  if(valid.Length==0)return data;
  await db.Database.CreateExecutionStrategy().ExecuteAsync(async()=>{
   db.ChangeTracker.Clear();await using var tx=await db.Database.BeginTransactionAsync(ct);
   await db.Database.ExecuteSqlRawAsync("DECLARE @r int; EXEC @r=sys.sp_getapplock @Resource='WorkJournal.MarketPulse',@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=15000; IF @r<0 THROW 51004,'Market snapshot busy',1;",ct);
   foreach(var market in valid){var row=await db.MarketPulseDays.SingleOrDefaultAsync(x=>x.TradeDate==market.Date&&x.Market==market.Market,ct);
    if(row==null){row=new(){TradeDate=market.Date,Market=market.Market};db.Add(row);}row.Json=JsonSerializer.Serialize(market);row.CapturedAt=clock.GetUtcNow();}
   await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);
  });return data;
 }
 public async Task<object> OverviewAsync(string market,CancellationToken ct)
 {
  var data=await CaptureAsync(ct);var selected=data.Markets.Where(x=>market=="all"||x.Market==market).ToArray();
  // Never add statistics from different market dates or pretend one market is a combined total.
  bool complete=selected.Length==(market=="all"?2:1)&&selected.Select(x=>x.Date).Distinct().Count()==1&&selected.All(x=>x.Stocks.Length>0);
  PulseMarket? aggregate=complete?Combine(selected,market):null;var history=new List<PulseMarket>();
  if(aggregate!=null){var rows=await db.MarketPulseDays.AsNoTracking().Where(x=>x.TradeDate<aggregate.Date&&(market=="all"||x.Market==market)).OrderByDescending(x=>x.TradeDate).Take(80).ToListAsync(ct);
   foreach(var group in rows.GroupBy(x=>x.TradeDate)){var day=group.Select(x=>JsonSerializer.Deserialize<PulseMarket>(x.Json)!).ToArray();if(day.Length==(market=="all"?2:1))history.Add(Combine(day,market));}}
  var industries=aggregate?.Stocks.Where(s=>s.ChangePercent.HasValue).GroupBy(s=>s.Industry).Select(g=>new{Name=g.Key,ChangePercent=g.Average(x=>x.ChangePercent),Up=g.Count(x=>x.ChangePercent>0),Count=g.Count(),Stocks=g.OrderByDescending(x=>x.ChangePercent).ToArray()}).OrderByDescending(x=>x.ChangePercent).ToArray();
  return new{market,indices=data.Markets.Select(x=>new{x.Market,x.Date,x.Index,x.IndexChange,x.IndexPercent}),summary=aggregate==null?null:new{aggregate.Date,aggregate.Up,aggregate.Down,aggregate.Flat,aggregate.LimitUp,aggregate.LimitDown,aggregate.Turnover},heat=aggregate==null?null:MarketHeatCalculator.Calculate(aggregate,history),industries,complete,data.UpdatedAt,data.Warnings};
 }
 static PulseMarket Combine(PulseMarket[] rows,string market){int? Sum(Func<PulseMarket,int?> f)=>rows.All(x=>f(x)!=null)?rows.Sum(x=>f(x)):null;return new(market,rows[0].Date,null,null,Sum(x=>x.Up),Sum(x=>x.Down),Sum(x=>x.Flat),Sum(x=>x.LimitUp),Sum(x=>x.LimitDown),rows.SelectMany(x=>x.Stocks).ToArray());}
}
