using WorkJournal.Web.Models;
namespace WorkJournal.Web.Services;
public static class SwingBacktester
{
    // Research model: next-session open, all-in long, fractional shares, no leverage.
    public static SwingBacktest Run(SwingHistory history, DateOnly start, decimal fee=0.001425m, decimal sellTax=0.003m)
    {
        var bars=SwingCalculator.Normalize(history.Bars);
        var result=new SwingBacktest();
        var eligible=bars.Where(x=>x.Date>=start).ToArray();
        if(eligible.Length==0) { result.Message="查詢期間沒有日行情。"; return result; }
        result.From=eligible[0].Date;result.To=eligible[^1].Date;result.Observations=eligible.Length;
        if(bars.Any(x=>x.Close is null or <=0 || x.Open is null or <=0))
        { result.Message="行情缺少有效開盤／收盤價，無法執行可靠回測。"; return result; }
        if(bars.Zip(bars.Skip(1)).Any(x=>Math.Abs(x.Second.Close!.Value/x.First.Close!.Value-1)>0.25m))
        { result.Message="期間含超過 25% 的單日價格跳動，可能涉及分割或減資；目前使用未還原行情，暫不提供績效，避免產生失真結果。"; return result; }
        decimal cash=1,units=0,entryCost=0,peak=1,maxDrawdown=0;
        int entryIndex=-1;
        bool enter=false,exit=false;
        string exitReason="";
        for(int i=0;i<bars.Length;i++)
        {
            var bar=bars[i];
            if(bar.Date<start) continue;
            if(exit && units>0)
            {
                cash=units*bar.Open!.Value*(1-fee-sellTax);
                result.Trades.Add(new(bars[entryIndex].Date,bar.Date,bars[entryIndex].Open!.Value,bar.Open.Value,(cash/entryCost-1)*100,exitReason));
                units=0;exit=false;
            }
            if(enter && units==0)
            {
                entryCost=cash;
                units=cash/(bar.Open!.Value*(1+fee));cash=0;entryIndex=i;
            }
            enter=false;
            var equity=cash+units*bar.Close!.Value;
            peak=Math.Max(peak,equity);
            maxDrawdown=Math.Max(maxDrawdown,(peak-equity)/peak);
            var snapshot=SwingCalculator.Calculate(bars.Take(i+1).ToArray(),history.Flows);
            if(snapshot.Score.HasValue) result.ScoredDays++; else result.MissingDays++;
            if(units>0 && (bar.Close<snapshot.MA20 || i-entryIndex+1>=20))
            {
                exit=true;exitReason=bar.Close<snapshot.MA20 ? "收盤跌破 MA20" : "持有滿 20 交易日";
            }
            else if(units==0 && snapshot.Score>=75) enter=true;
        }
        if(result.ScoredDays==0) { result.Message="期間內沒有完整 100 分評分資料，無法建立回測訊號。"; return result; }
        result.OpenPositions=units>0?1:0;
        result.TotalReturn=(cash+units*bars[^1].Close!.Value-1)*100;
        result.MaxDrawdown=maxDrawdown*100;
        if(result.Trades.Count>0)
        {
            result.WinRate=result.Trades.Count(x=>x.ReturnPercent>0)*100m/result.Trades.Count;
            result.AverageReturn=result.Trades.Average(x=>x.ReturnPercent);
        }
        result.Message="歷史模擬：完整評分 ≥ 75 的次日開盤進場；跌破 MA20 或持有滿 20 交易日，次日開盤出場。期間末未平倉按收盤估值。";
        return result;
    }
}
