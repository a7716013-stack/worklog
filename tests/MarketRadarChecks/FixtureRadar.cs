using WorkJournal.Web.Models;
using WorkJournal.Web.Services;
public class FixtureRadar : ITaiwanMarketRankingProvider, IMarketRadarHistoryProvider
{
    public static readonly DateOnly Day=new(2026,10,5);
    public int Calls;
    public bool FailOne { get; set; }
    public Task<MarketSnapshot> GetAsync(CancellationToken ct) => Task.FromResult(new MarketSnapshot([
        new(new("2330","台積電","twse",false),Day,110,10,3000),
        new(new("006208","富邦台50","twse",true),Day,90,-10,1000),
        new(new("0050","元大台灣50","twse",true),Day,100,0,2000),
        new(new("6488","測試上櫃","tpex",false),Day,108,8,5000),
        new(new("1234","較舊日期","twse",false),Day.AddDays(-1),10,1,999999)
    ],[new("2330","<img src=x onerror=alert(1)> 法說會","法說會",new(2026,10,5,10,0,0,TimeSpan.FromHours(8)),"TWSE","javascript:alert(1)","Unknown","<script>alert(1)</script>"),
        new("9999","無收盤報價的公告","重大訊息",new(2026,10,5,9,0,0,TimeSpan.FromHours(8)),"TWSE","https://example.test/announcement","Unknown","Suspended stock fixture")],[],DateTimeOffset.UtcNow));
    public async Task<RadarHistory> GetAsync(MarketQuote quote,CancellationToken ct)
    {
        Interlocked.Increment(ref Calls);await Task.Delay(10,ct);
        if(FailOne && quote.Stock.Symbol=="6488")throw new HttpRequestException("synthetic provider error with SECRET");
        return History(quote.Date);
    }
    public static RadarHistory History(DateOnly day)=>new(Enumerable.Range(1,80).Select(i=>new SwingBar(day.AddDays(i-81),80+i/4m,81+i/4m,79+i/4m,80+i/4m,1000)).ToArray(),
        [new(day,1000,100)],25,day.AddMonths(-1));
}
