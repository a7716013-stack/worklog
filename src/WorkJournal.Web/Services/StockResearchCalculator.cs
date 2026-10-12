using WorkJournal.Web.Models;
namespace WorkJournal.Web.Services;
public record ResearchPoint(DateOnly Date,decimal? Open,decimal? High,decimal? Low,decimal? Close,decimal? Volume,decimal? MA5,decimal? MA20,decimal? MA60,decimal? VolumeMA5,decimal? VolumeMA20);
public record ResearchFlow(string Name,decimal? Day,decimal? Days5,decimal? Days20);
public record StockResearchData(ResearchPoint[] Chart,SwingSnapshot Strategy,ResearchFlow[] Flows,string ShortTrend,string MediumTrend,string Message);
public static class StockResearchCalculator
{
    public static StockResearchData Calculate(SwingHistory history)
    {
        var bars=SwingCalculator.Normalize(history.Bars);
        decimal? Volume(int i,int n) {
            if(i<n)return null;var window=bars.Skip(i-n).Take(n).ToArray();
            return window.All(x=>x.Volume>=0)?window.Average(x=>x.Volume):null;
        }
        var chart=bars.Select((b,i)=>{
            var t=TechnicalIndicators.Calculate(bars.Take(i+1).Select(x=>new DailyClose(x.Date,x.Close)));
            return new ResearchPoint(b.Date,b.Open,b.High,b.Low,b.Close,b.Volume,t.MA5,t.MA20,t.MA60,Volume(i,5),Volume(i,20));
        }).ToArray();
        var dated=history.Flows.GroupBy(x=>x.Date).ToDictionary(g=>g.Key,g=>g.Last());
        decimal? Sum(int n,Func<SwingFlow,decimal?> select) {
            if(bars.Length<n)return null;decimal sum=0;
            foreach(var b in bars.TakeLast(n)) {
                if(!dated.TryGetValue(b.Date,out var f)||select(f) is not decimal value)return null;sum+=value;
            }return sum;
        }
        string Trend(int period) {
            if(chart.Length<2)return "資料不足";var last=chart[^1];var prev=chart[^2];
            var ma=period==20?last.MA20:last.MA60;var old=period==20?prev.MA20:prev.MA60;
            if(ma==null||old==null||last.Close==null)return "資料不足";
            return last.Close>ma&&ma>old?"偏多":last.Close<ma&&ma<old?"偏空":"整理";
        }
        return new(chart,SwingCalculator.Calculate(bars,history.Flows),[
            new("外資",Sum(1,x=>x.Foreign),Sum(5,x=>x.Foreign),Sum(20,x=>x.Foreign)),
            new("投信",Sum(1,x=>x.Trust),Sum(5,x=>x.Trust),Sum(20,x=>x.Trust)),
            new("自營商",Sum(1,x=>x.Dealer),Sum(5,x=>x.Dealer),Sum(20,x=>x.Dealer))],Trend(20),Trend(60),history.Message);
    }
}
