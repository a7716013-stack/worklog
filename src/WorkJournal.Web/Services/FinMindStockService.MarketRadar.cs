using WorkJournal.Web.Models;
namespace WorkJournal.Web.Services;

public partial class FinMindStockService
{
    public async Task<RadarHistory> RadarHistoryAsync(string symbol, DateOnly date, bool isEtf, CancellationToken ct)
    {
        var prices=await ReadAsync("TaiwanStockPrice",symbol,date.AddDays(-180),date,ct);
        var bars=prices.Where(x=>Text(x,"stock_id")==symbol && ParseDate(x) is { } d && d<=date)
            .Select(x=>new SwingBar(ParseDate(x)!.Value,Number(x,"open"),Number(x,"max"),Number(x,"min"),Number(x,"close"),Number(x,"Trading_Volume"))).ToArray();
        SwingFlow[] flows=[]; decimal? yoy=null; DateOnly? month=null;
        try { flows=ParseSwingFlows(await ReadAsync("TaiwanStockInstitutionalInvestorsBuySell",symbol,date.AddDays(-10),date,ct),symbol); }
        catch(Exception e) when(OptionalFailure(e,ct)) { }
        if(!isEtf)
        {
            try
            {
                var rows=await ReadAsync("TaiwanStockMonthRevenue",symbol,date.AddMonths(-16),date,ct);
                var periods=rows.Where(x=>Text(x,"stock_id")==symbol && ParseDate(x) is { } published && published<=date && RevenuePeriod(x) is { } m && m<new DateOnly(date.Year,date.Month,1))
                    .GroupBy(x=>RevenuePeriod(x)!.Value).ToDictionary(x=>x.Key,x=>x.OrderByDescending(ParseDate).First());
                if(periods.Count>0)
                {
                    month=periods.Keys.Max();
                    if(month>=new DateOnly(date.Year,date.Month,1).AddMonths(-2) && periods.TryGetValue(month.Value.AddYears(-1),out var previous)) yoy=Growth(Number(periods[month.Value],"revenue"),Number(previous,"revenue"));
                }
            }
            catch(Exception e) when(OptionalFailure(e,ct)) { }
        }
        return new(bars,flows,yoy,month);
    }
}
