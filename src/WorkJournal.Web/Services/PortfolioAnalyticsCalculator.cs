using WorkJournal.Web.Models;
namespace WorkJournal.Web.Services;
public record ClosedInvestment(string Symbol,DateTimeOffset OpenedAt,DateTimeOffset ClosedAt,decimal Invested,decimal Profit,decimal ReturnPercent);
public record PortfolioStatistics(int Completed,int Wins,int Losses,int Flat,decimal? WinRate,decimal? ProfitFactor,decimal? AverageWin,decimal? AverageLoss,decimal? PayoffRatio,ClosedInvestment[] Investments);
public static class PortfolioAnalyticsCalculator
{
 public static PortfolioStatistics Calculate(IEnumerable<PaperTrade> trades)
 {
  var closed=new List<ClosedInvestment>();
  foreach(var group in trades.GroupBy(x=>x.StockId)){
   long quantity=0;decimal invested=0,flow=0;DateTimeOffset opened=default;bool valid=true;
   foreach(var trade in group.OrderBy(x=>x.ExecutedAt).ThenBy(x=>x.Id)){
    if(trade.Quantity<=0){valid=false;continue;}
    if(trade.Side==PaperOrderSide.Buy){if(quantity==0){opened=trade.ExecutedAt;invested=0;flow=0;valid=true;}quantity+=trade.Quantity;invested-=trade.NetCashAmount;}
    else{if(trade.Quantity>quantity)valid=false;quantity-=trade.Quantity;}
    flow+=trade.NetCashAmount;
    if(quantity==0&&valid&&invested>0)closed.Add(new(group.Key,opened,trade.ExecutedAt,invested,flow,flow/invested*100));
   }
  }
  var wins=closed.Where(x=>x.Profit>0).ToArray();var losses=closed.Where(x=>x.Profit<0).ToArray();
  decimal? win=wins.Length>0?wins.Average(x=>x.ReturnPercent):null,loss=losses.Length>0?losses.Average(x=>x.ReturnPercent):null;
  return new(closed.Count,wins.Length,losses.Length,closed.Count-wins.Length-losses.Length,closed.Count>0?wins.Length*100m/closed.Count:null,losses.Length>0?wins.Sum(x=>x.Profit)/-losses.Sum(x=>x.Profit):null,win,loss,win.HasValue&&loss<0?win/-loss:null,closed.OrderByDescending(x=>x.ClosedAt).ToArray());
 }
 public static decimal? Drawdown(IEnumerable<PortfolioDailyValue> samples)
 {
  var rows=samples.OrderBy(x=>x.TradeDate).ToArray();if(rows.Length<2)return null;
  decimal peak=rows[0].TotalAssets,drawdown=0;foreach(var row in rows){peak=Math.Max(peak,row.TotalAssets);if(peak>0)drawdown=Math.Min(drawdown,(row.TotalAssets/peak-1)*100);}return drawdown;
 }
}
