using System.Text.Json;
using WorkJournal.Web.Models;
using WorkJournal.Web.Services;
static class SwingChecks
{
    static void Check(bool condition,string message){if(!condition)throw new Exception(message);}
    public static void Run()
    {
        var start=new DateOnly(2025,1,1);
        var bars=Enumerable.Range(0,100).Select(i=>new SwingBar(start.AddDays(i),100+i,101+i,99+i,100.5m+i,1000)).ToArray();
        var flows=bars.Select(x=>new SwingFlow(x.Date,100,-50)).ToArray();
        var s=SwingCalculator.Calculate(bars,flows);
        Check(s.MA10==195m && s.MA20==190m && s.MA60==170m,"Swing averages");
        Check(s.Rules.Where(x=>x.Points>0).Sum(x=>x.Points)==100,"Score positive weights must total 100");
        Check(s.Foreign5==500 && s.Trust20==-1000 && s.ForeignStreak==20 && s.TrustStreak==0,"Trading-session flow windows and streaks");
        Check(s.Rules.Single(x=>x.Group=="突破").Passed==true,"Breakout must compare prior highs, not today's high");
        Check(s.PriorHigh20==199 && s.VolumeRatio==1,"Previous 20 sessions exclude today");
        var unknown=SwingCalculator.Calculate(bars,flows.SkipLast(1).ToArray());
        Check(unknown.Score==null && unknown.Foreign5==null && unknown.ForeignStreak==null,"Missing latest flow must not become zero or complete score");
        Check(SwingCalculator.Calculate(bars,[]).Score==null,"No invented score when all flow data missing");
        Check(SwingCalculator.Calculate(bars.Take(10).ToArray(),flows).Score==null,"Warmup insufficient");
        var huge=bars.ToArray();huge[^1]=huge[^1] with {Volume=40000,High=220};
        s=SwingCalculator.Calculate(huge,flows);
        Check(s.VolumeRatio==40 && s.PriorHigh20==199,"Current volume and high excluded from baseline");
        var future=flows.Append(new SwingFlow(start.AddDays(1000),1000000,1000000)).ToArray();
        Check(SwingCalculator.Calculate(bars,future).Foreign5==500,"Future institutional records excluded");
        var malformed=bars.ToArray();malformed[^2]=malformed[^2] with {Close=null};
        Check(SwingCalculator.Calculate(malformed,flows).MA20==null,"Do not bridge invalid price");
        using var json=JsonDocument.Parse("""
        [{"date":"2025-01-01","stock_id":"2330","name":"Foreign_Investor","buy":1000,"sell":200},
        {"date":"2025-01-01","stock_id":"2330","name":"Foreign_Dealer_Self","buy":9000,"sell":0},
        {"date":"2025-01-01","stock_id":"2330","name":"Investment_Trust","buy":50,"sell":100},
        {"date":"2025-01-01","stock_id":"2317","name":"Foreign_Investor","buy":99999,"sell":0}]
        """);
        var f=FinMindStockService.ParseSwingFlows(json.RootElement.EnumerateArray(),"2330").Single();
        Check(f.Foreign==800 && f.Trust==-50,"Net shares and correct institutional groups");
        var strongFlows=bars.Select(x=>new SwingFlow(x.Date,100,100)).ToArray();
        bars[60]=bars[60] with {Volume=3000};
        var history=new SwingHistory(new("2330","Test","twse",false),bars,strongFlows,"");
        var back=SwingBacktester.Run(history,start.AddDays(60),0,0);
        Check(back.Trades.Count>0,"Synthetic trend should produce backtest trades");
        Check(back.Trades[0].Entry==start.AddDays(61) && back.Trades[0].EntryPrice==bars[61].Open,"Signal on day 60 fills next day's open");
        Check(back.Trades[0].Exit==start.AddDays(81),"20-session exit triggers subsequent open");
        Check(back.WinRate==100 && back.TotalReturn>0,"Positive trend return");
        var fees=SwingBacktester.Run(history,start.AddDays(60));
        Check(fees.TotalReturn<back.TotalReturn,"Fees reduce strategy return");
        var noFlows=SwingBacktester.Run(history with {Flows=[]},start.AddDays(60));
        Check(noFlows.TotalReturn==null,"Backtest cannot produce performance without scored days");
        var split=bars.ToArray();split[80]=split[80] with {Close=50};
        Check(SwingBacktester.Run(history with {Bars=split},start.AddDays(60)).TotalReturn==null,"Unadjusted split must block misleading performance");
        var truncated=SwingBacktester.Run(history with {Bars=bars.Take(85).ToArray()},start.AddDays(60),0,0);
        Check(truncated.Trades[0]==back.Trades[0],"Appending future prices must not change an earlier completed trade");
        Check(back.MaxDrawdown>=0 && back.MaxDrawdown<=100,"Drawdown bounds");
        Console.WriteLine("PASS: swing scoring, prior-only breakout/volume, flow windows, missing data, no-lookahead fills, fees and corporate-action guard.");
    }
}
