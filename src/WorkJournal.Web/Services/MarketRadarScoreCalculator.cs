using WorkJournal.Web.Models;
namespace WorkJournal.Web.Services;

public static class MarketRadarScoreCalculator
{
    public static MarketRadarStock Calculate(MarketQuote quote, RadarHistory history, MarketEvent[] events)
    {
        var prior=history.Bars.Where(x=>x.Date<quote.Date).GroupBy(x=>x.Date).Select(x=>x.Last()).OrderBy(x=>x.Date).ToArray();
        decimal? Average(int count)
        {
            var sample=prior.TakeLast(count).ToArray();
            return sample.Length==count && sample.All(x=>x.Volume>=0 && x.Close>0) ? sample.Average(x=>x.Volume!.Value) : null;
        }
        var avg5=Average(5); var avg20=Average(20); var ratio=avg20>0?quote.Volume/avg20:null;
        var closes=prior.Select(x=>new DailyClose(x.Date,x.Close)).Append(new(quote.Date,quote.Close)).ToArray();
        var t=TechnicalIndicators.Calculate(closes); var previous=TechnicalIndicators.Calculate(closes.SkipLast(1));
        var flow=history.Flows.LastOrDefault(x=>x.Date==quote.Date);
        var risks=new List<string>(); var reasons=new List<string>();
        var change=quote.ChangePercent;
        int? momentum=change is null?null:change>=7?20:change>=5?18:change>=3?15:change>=1?10:change>=0?6:change>=-3?2:0;
        int? volume=ratio is null?null:ratio>=5?20:ratio>=3?17:ratio>=2?14:ratio>=1.5m?10:ratio>=1?6:2;
        // An abnormal volume spike on a falling day is not bullish confirmation.
        if(change<0 && volume.HasValue) volume=Math.Min(volume.Value,5);
        int? technical=t.MA5 is null || t.MA20 is null || t.MA60 is null || t.RSI14 is null || t.Histogram is null ? null :
            (quote.Close>t.MA20?5:0)+(t.MA5>t.MA20?3:0)+(t.MA20>t.MA60?3:0)+(quote.Close>t.MA60?2:0)+(t.Histogram>0?4:0)+(t.RSI14 is >=50 and <=70?3:0);
        int? chips=flow?.Foreign is null || flow.Trust is null ? null : (flow.Foreign>0?10:0)+(flow.Trust>0?5:0);
        int? fundamental=quote.Stock.IsEtf || history.RevenueYoY is null ? null : history.RevenueYoY>=20?10:history.RevenueYoY>=10?8:history.RevenueYoY>=0?5:0;
        // Event points measure verified attention, never infer positive impact from a headline.
        var relevant=events.Where(x=>x.StockId==quote.Stock.Symbol && DateOnly.FromDateTime(x.PublishedAt.Date)>=quote.Date.AddDays(-7) && DateOnly.FromDateTime(x.PublishedAt.Date)<=quote.Date).ToArray();
        int? eventPoints=relevant.Length==0?null:relevant.Any(x=>x.Category is "營收" or "法說會")?15:10;
        var macd=t.Histogram is null?"資料不足":t.Histogram>0 && previous.Histogram<=0?"黃金交叉":t.Histogram<0 && previous.Histogram>=0?"死亡交叉":t.Histogram>0?"多頭區間":t.Histogram<0?"空頭區間":"持平";
        if(change.HasValue) reasons.Add($"當日漲跌 {change:+0.00;-0.00;0.00}%");
        if(ratio.HasValue) reasons.Add($"成交量為前 20 日均量 {ratio:0.00} 倍");
        if(quote.Close>t.MA20) reasons.Add("收盤站上 MA20");
        if(prior.Length>=20 && prior.TakeLast(20).All(x=>x.High>0) && quote.Close>prior.TakeLast(20).Max(x=>x.High)) reasons.Add("突破前 20 交易日高點");
        if(flow?.Foreign>0) reasons.Add("當日外資買超");
        if(macd=="黃金交叉") reasons.Add("MACD 黃金交叉");
        if(t.RSI14>80) risks.Add("技術過熱");
        if(t.RSI14<30) risks.Add("技術超跌（不代表買點）");
        if(change<=-3) risks.Add("大幅下跌");
        if(ratio>=3) risks.Add("異常爆量");
        if(change<0 && ratio>=1.5m) risks.Add("放量下跌／量價背離");
        if(relevant.Length>0) risks.Add("事件影響待確認");
        if(quote.Stock.IsEtf && (quote.Stock.Name.Contains("反") || quote.Stock.Name.Contains("正2"))) risks.Add("槓桿／反向 ETF");
        var parts=new RadarScorePart[] {
            new("價格動能",20,momentum,"當日漲幅 ≥7/5/3/1/0/-3%：20/18/15/10/6/2 分；更低 0 分。"),
            new("成交量異常",20,volume,"20 日量比 ≥5/3/2/1.5/1：20/17/14/10/6 分；低於 1 為 2 分，下跌日上限 5 分。"),
            new("技術面",20,technical,"站上 MA20 +5、MA5>MA20 +3、MA20>MA60 +3、站上 MA60 +2、MACD 柱為正 +4、RSI 50～70 +3。"),
            new("籌碼面",15,chips,"同一交易日外資買超 +10、投信買超 +5；缺資料不計分。"),
            new("基本面",10,fundamental,quote.Stock.IsEtf?"ETF 不套用公司營收；基金基本面尚未接入，列資料不足。":$"最新可用月營收 {history.RevenueMonth:yyyy-MM}，年增 ≥20/10/0%：10/8/5 分；負成長 0 分。"),
            new("重大資訊",15,eventPoints,"近 7 日可確認營收／法說事件 15 分、其他公告 10 分；僅代表研究關注度，非利多。缺公告不當成無風險。") };
        if(parts.Any(x=>x.Points is null)) risks.Add("資料不足／評分不完整");
        return new() {Quote=quote,AverageVolume5=avg5,AverageVolume20=avg20,Technical=t,ForeignNet=flow?.Foreign,
            MacdState=macd,Parts=parts,Reasons=reasons.Take(5).ToArray(),RiskFlags=risks.ToArray(),Events=relevant};
    }
}
