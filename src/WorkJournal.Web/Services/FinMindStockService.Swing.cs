using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Caching.Memory;
using WorkJournal.Web.Models;
namespace WorkJournal.Web.Services;
public partial class FinMindStockService
{
    private static readonly SemaphoreSlim SwingGate=new(2,2);
    private static readonly SemaphoreSlim BacktestGate=new(1,1);
    public async Task<SwingBacktest> SwingBacktestAsync(string symbol,CancellationToken ct)
    {
        var key="swing:backtest:"+symbol;
        if(cache.TryGetValue<SwingBacktest>(key,out var hit)) return hit!;
        var history=await SwingHistoryAsync(symbol,true,ct);
        await BacktestGate.WaitAsync(ct);
        try
        {
            if(cache.TryGetValue<SwingBacktest>(key,out hit)) return hit!;
            ct.ThrowIfCancellationRequested();
            var today=DateOnly.FromDateTime(DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(8)).DateTime);
            if (marketData != null && (history.Bars.Any(x => x.Volume == null) ||
                history.Bars.Where(x => x.Date >= today.AddYears(-5)).Any(x => !history.Flows.Any(f => f.Date == x.Date && f.Foreign != null && f.Trust != null))))
                return new SwingBacktest { Message = "歷史成交量或法人資料尚未完整，暫不計算回測績效。日常波段分析仍可使用官方資料；請待歷史來源恢復後補齊。" };
            var result=SwingBacktester.Run(history,today.AddYears(-5),sellTax:history.Stock.IsEtf?0.001m:0.003m);
            cache.Set(key,result,TimeSpan.FromMinutes(30));
            return result;
        }
        finally { BacktestGate.Release(); }
    }
    public async Task<SwingStock[]> SwingCatalogAsync(CancellationToken ct)
    {
        const string key="swing:catalog";
        if(cache.TryGetValue<SwingStock[]>(key,out var hit)) return hit!;
        var rows=await ReadAsync("TaiwanStockInfo","",null,null,ct);
        var result=rows.Where(x=>Text(x,"type") is "twse" or "tpex" && Regex.IsMatch(Text(x,"stock_id")??"",@"^[0-9]{4}[0-9A-Z]{0,2}$"))
            .GroupBy(x=>Text(x,"stock_id")!).Select(g=>g.OrderByDescending(ParseDate).First())
            .Select(x=>new SwingStock(Text(x,"stock_id")!,Text(x,"stock_name")??"",Text(x,"type")!,IsEtfCategory(Text(x,"industry_category")))).ToArray();
        cache.Set(key,result,TimeSpan.FromHours(12));
        return result;
    }
    public async Task<SwingHistory> SwingHistoryAsync(string symbol, bool backtest, CancellationToken ct)
    {
        var key="swing:history:"+symbol+":"+backtest;
        if(cache.TryGetValue<SwingHistory>(key,out var hit)) return hit!;
        await SwingGate.WaitAsync(ct);
        try
        {
            if(cache.TryGetValue<SwingHistory>(key,out hit)) return hit!;
            var stock=(await SwingCatalogAsync(ct)).FirstOrDefault(x=>x.Symbol==symbol) ?? throw new InvalidDataException("查無此上市櫃股票。");
            var today=DateOnly.FromDateTime(DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(8)).DateTime);
            var from=backtest ? today.AddYears(-5).AddDays(-150) : today.AddDays(-420);
            var priceTask=ReadAsync("TaiwanStockPrice",symbol,from,today,ct);
            var flowTask=Flows();
            await Task.WhenAll(priceTask,flowTask);
            var rows=await priceTask;
            var (flows,message)=await flowTask;
            if (marketData != null)
            {
                var sources = rows.Select(x => Text(x,"_source")).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct();
                message = "行情來源：" + string.Join("、",sources) + "；非即時行情，已啟用本機共用持久快取。" + message;
                if (rows.Any(x => Text(x,"_source") == "TPEx.Monthly.PriceOnly"))
                    message += " 上櫃備援較舊月份僅提供價格，成交量缺值保留，不可用於完整量價回測。";
            }
            var bars=rows.Where(x=>Text(x,"stock_id")==symbol && ParseDate(x) is DateOnly d && d<=today && d>=from)
                .Select(x=>new SwingBar(ParseDate(x)!.Value,Number(x,"open"),Number(x,"max"),Number(x,"min"),Number(x,"close"),Number(x,"Trading_Volume")));
            var result=new SwingHistory(stock,SwingCalculator.Normalize(bars),flows,message);
            cache.Set(key,result,TimeSpan.FromMinutes(backtest?30:5));
            return result;
            async Task<(SwingFlow[],string)> Flows()
            {
                try
                {
                    var rows=await ReadAsync("TaiwanStockInstitutionalInvestorsBuySell",symbol,marketData != null && !backtest ? today.AddDays(-45) : from,today,ct);
                    var sources = rows.Select(x => Text(x,"_source")).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct();
                    return (ParseSwingFlows(rows,symbol),marketData == null ? "FinMind 日行情／法人資料；非盤中即時報價，快取 5 分鐘。" : " 法人來源：" + string.Join("、",sources) + "。");
                }
                catch(Exception ex) when(OptionalFailure(ex,ct))
                { return ([], "法人資料暫時無法取得；保留技術分析，完整評分暫不提供。"); }
            }
        }
        finally { SwingGate.Release(); }
    }
    public static SwingFlow[] ParseSwingFlows(IEnumerable<JsonElement> rows,string symbol) =>
        rows.Where(x=>Text(x,"stock_id")==symbol && ParseDate(x)!=null)
        .GroupBy(x=>ParseDate(x)!.Value).OrderBy(g=>g.Key).Select(g=>
        {
            decimal? Net(string name)
            {
                var row=g.LastOrDefault(x=>Text(x,"name")==name);
                return row.ValueKind==JsonValueKind.Undefined ? null : Number(row,"buy")-Number(row,"sell");
            }
            var dealer = Net("Dealer");
            // FinMind may split dealers into self and hedge. Never treat a missing half as zero.
            dealer ??= Net("Dealer_self") + Net("Dealer_Hedging");
            return new SwingFlow(g.Key,Net("Foreign_Investor"),Net("Investment_Trust"),dealer);
        }).ToArray();
}
