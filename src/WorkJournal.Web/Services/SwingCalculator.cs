using WorkJournal.Web.Models;
namespace WorkJournal.Web.Services;

public static class SwingCalculator
{
    private static bool? Compare(decimal? a, decimal? b, Func<decimal, decimal, bool> test) => a.HasValue && b.HasValue ? test(a.Value,b.Value) : null;
    private static bool? Above(decimal? a, decimal? b) => Compare(a,b,(x,y)=>x>y);
    private static bool? All(params bool?[] values) => values.Any(x=>x == false) ? false : values.Any(x=>x == null) ? null : true;
    public static SwingBar[] Normalize(IEnumerable<SwingBar> input) => input.GroupBy(x=>x.Date).Select(g=>g.Last()).OrderBy(x=>x.Date).ToArray();
    public static SwingSnapshot Calculate(IReadOnlyList<SwingBar> input, IReadOnlyList<SwingFlow> flows)
    {
        var bars=Normalize(input);
        // Indicator windows restart after an invalid close instead of bridging a missing session.
        var invalid=Array.FindLastIndex(bars,x=>x.Close is null or <=0);
        bars=bars.Skip(invalid+1).ToArray();
        if(bars.Length==0) return new();
        var last=bars[^1]; var previous=bars.Length>1 ? bars[^2] : null;
        var closes=bars.Select(x=>new DailyClose(x.Date,x.Close)).ToArray();
        var t=TechnicalIndicators.Calculate(closes);
        var prev=TechnicalIndicators.Calculate(closes.SkipLast(1));
        decimal? Mean(int period, int skip=0) => bars.Length>=period+skip ? bars.Take(bars.Length-skip).TakeLast(period).Average(x=>x.Close) : null;
        var prior20=bars.SkipLast(1).TakeLast(20).ToArray();
        decimal? high=prior20.Length==20 && prior20.All(x=>x.High>0) ? prior20.Max(x=>x.High) : null;
        decimal? low=prior20.Length==20 && prior20.All(x=>x.Low>0) ? prior20.Min(x=>x.Low) : null;
        decimal? volume=prior20.Length==20 && prior20.All(x=>x.Volume>=0) ? prior20.Average(x=>x.Volume) : null;
        var ratio=volume>0 && last.Volume>=0 ? last.Volume/volume : null;
        var ma10=Mean(10);
        var datedFlows=flows.Where(x=>x.Date<=last.Date).GroupBy(x=>x.Date).ToDictionary(x=>x.Key,x=>x.Last());
        decimal? Sum(int n, bool foreign)
        {
            if(bars.Length<n) return null;
            decimal result=0;
            foreach(var bar in bars.TakeLast(n))
            {
                if(!datedFlows.TryGetValue(bar.Date,out var f)) return null;
                var value=foreign ? f.Foreign : f.Trust;
                if(value==null) return null;
                result+=value.Value;
            }
            return result;
        }
        int? Streak(bool foreign)
        {
            int count=0;
            foreach(var bar in bars.TakeLast(20).Reverse())
            {
                if(!datedFlows.TryGetValue(bar.Date,out var f)) return null;
                var v=foreign ? f.Foreign : f.Trust;
                if(v==null) return null;
                if(v<=0) break;
                count++;
            }
            return count;
        }
        var foreign5=Sum(5,true); var trust5=Sum(5,false);
        var above=Above(last.Close,t.MA20);
        var aligned=All(Above(t.MA5,ma10),Above(ma10,t.MA20));
        var rising=Above(t.MA20,prev.MA20);
        var rsi=t.RSI14.HasValue ? t.RSI14>=50 && t.RSI14<=70 : (bool?)null;
        var macd=Above(t.MACD,t.Signal);
        var breakout=Above(last.Close,high);
        var expanded=Above(ratio,1.5m);
        var below=Compare(last.Close,t.MA20,(a,b)=>a<b);
        var longBlack=All(Compare(last.Close,last.Open,(a,b)=>a<=b*0.97m),Above(ratio,2m));
        var rules=new List<SwingRule> {
            new("趨勢","收盤價 > MA20",10,above),
            new("趨勢","MA5 > MA10 > MA20",15,aligned),
            new("趨勢","MA20 高於前一交易日",10,rising),
            new("動能","RSI14 介於 50～70",10,rsi),
            new("動能","MACD DIF > 訊號線",10,macd),
            new("突破","收盤突破前 20 日最高價（不含今日）",15,breakout),
            new("成交量","今日量 > 前 20 日均量 × 1.5",10,expanded),
            new("籌碼","近 5 交易日外資累計買超",10,Above(foreign5,0)),
            new("籌碼","近 5 交易日投信累計買超",10,Above(trust5,0)),
            new("風險","收盤跌破 MA20",-15,below),
            new("風險","黑 K 實體 ≥ 3% 且量比 > 2",-15,longBlack)
        };
        var earned=rules.Where(x=>x.Passed==true).Sum(x=>x.Points);
        var complete=rules.All(x=>x.Passed!=null);
        int? score=complete ? Math.Clamp(earned,0,100) : null;
        var crossover=All(macd,Compare(prev.MACD,prev.Signal,(a,b)=>a<=b));
        var rebound=All(Above(last.Close,t.MA5),Compare(previous?.Close,prev.MA5,(a,b)=>a<=b));
        var pullback=All(Above(t.MA20,t.MA60),rising,
            Compare(previous?.Low,prev.MA20,(a,b)=>a>=b*0.98m),
            Compare(previous?.Close,Mean(10,1),(a,b)=>a<=b*1.02m),rebound);
        var rsiUp=Above(t.RSI14,prev.RSI14);
        return new SwingSnapshot {
            Date=last.Date,Close=last.Close,ChangePercent=previous?.Close>0 ? (last.Close/previous.Close-1)*100 : null,
            Score=score,Earned=Math.Clamp(earned,0,100),Available=rules.Where(x=>x.Points>0 && x.Passed!=null).Sum(x=>x.Points),
            State=score==null ? "資料不足" : below==true || longBlack==true ? "風險升高" : score>=75 ? "優先研究" : score>=50 ? "持續觀察" : "條件未齊",
            MA5=t.MA5,MA10=ma10,MA20=t.MA20,MA60=t.MA60,RSI=t.RSI14,Macd=t.MACD,Signal=t.Signal,
            VolumeRatio=ratio,AverageVolume=volume,PriorHigh20=high,PriorLow20=low,
            Foreign5=foreign5,Trust5=trust5,Foreign20=Sum(20,true),Trust20=Sum(20,false),
            ForeignStreak=Streak(true),TrustStreak=Streak(false),
            FlowDate=datedFlows.Count>0 ? datedFlows.Keys.Max() : null, Rules=rules,
            Strategies=[
                new("均線多頭排列","收盤 > MA5 > MA10 > MA20，且 MA20 上升",All(Above(last.Close,t.MA5),aligned,rising)),
                new("均線回檔轉強","MA20 > MA60 且上升；前日低點守 MA20 的 98%、收盤 ≤ MA10 的 102%；今日重新站上 MA5",pullback),
                new("放量突破","收盤突破前 20 日高點、量比 > 1.5、MA20 上升",All(breakout,expanded,rising)),
                new("MACD 黃金交叉","DIF 今日上穿訊號線，收盤 > MA20 且 MA20 上升",All(crossover,above,rising)),
                new("RSI 動能轉強","RSI14 > 50 且上升，收盤 > MA20",All(Above(t.RSI14,50),rsiUp,above)),
                new("縮量回檔後放量","前日黑 K 且低於前 20 日均量；今日紅 K 且成交量高於前日",All(Compare(previous?.Close,previous?.Open,(a,b)=>a<b),Compare(previous?.Volume,volume,(a,b)=>a<b),Above(last.Close,last.Open),Above(last.Volume,previous?.Volume))),
                new("法人順勢買超","收盤 > MA20、MA20 上升、投信連買至少 3 日且近 5 日外資＋投信買超",All(above,rising,Streak(false) is int days ? days>=3 : null,Above(foreign5+trust5,0)))
            ]
        };
    }
    public static List<SwingChartPoint> Chart(SwingBar[] bars)
    {
        var points=new List<SwingChartPoint>();
        for(int i=Math.Max(0,bars.Length-90);i<bars.Length;i++)
        {
            var prefix=bars.Take(i+1).ToArray();
            var t=TechnicalIndicators.Calculate(prefix.Select(x=>new DailyClose(x.Date,x.Close)));
            var b=bars[i];
            var ma10=t.Samples>=10 ? prefix.TakeLast(10).Average(x=>x.Close) : null;
            points.Add(new(b.Date,b.Open,b.High,b.Low,b.Close,b.Volume,t.MA5,ma10,t.MA20,t.MA60,t.RSI14,t.MACD,t.Signal,t.Histogram));
        }
        return points;
    }
}
