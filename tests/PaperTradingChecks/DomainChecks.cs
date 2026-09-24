using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WorkJournal.Web.Data;
using WorkJournal.Web.Models;
using WorkJournal.Web.Services;
using WorkJournal.Web.ViewModels;

public static class DomainChecks
{
    static int checks;
    static void Check(bool ok, string name) { if (!ok) throw new Exception("FAIL: " + name); checks++; Console.WriteLine("PASS: " + name); }
    public static async Task Run(DbContextOptions<JournalDbContext> dbOptions)
    {
        var market = new FixtureMarket();
        async Task<T> Use<T>(Func<PaperTradingService, JournalDbContext, Task<T>> action, SaveChangesInterceptor? interceptor = null)
        {
            var configured = interceptor == null ? dbOptions : new DbContextOptionsBuilder<JournalDbContext>(dbOptions).AddInterceptors(interceptor).Options;
            await using var db = new JournalDbContext(configured);
            using var memory = new MemoryCache(new MemoryCacheOptions());
            using var http = new HttpClient(market, false) { BaseAddress = new Uri("https://fixture.test/") };
            var quotes = new FinMindStockService(http, memory, new ConfigurationBuilder().Build(), NullLogger<FinMindStockService>.Instance);
            var service = new PaperTradingService(db, quotes, Options.Create(new PaperTradingOptions()));
            return await action(service, db);
        }
        Task<PaperPortfolioViewModel> Portfolio() => Use((s, d) => s.GetPortfolioAsync(default));
        var accounts = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => Use((s, d) => s.GetAccountAsync(default))));
        var account = accounts[0];
        Check(accounts.All(x => x.Id == 1 && x.Cash == 1000000m && x.Generation == account.Generation), "concurrent first visit creates exactly one default account");
        PaperOrderViewModel Input(PaperOrderSide side = PaperOrderSide.Buy, int quantity = 1000, PaperOrderType type = PaperOrderType.Market, decimal? limit = null, string symbol = "2330") =>
            new() { StockId = symbol, Side = side, Quantity = quantity, OrderType = type, LimitPrice = limit, AccountGeneration = account.Generation };
        Task<PaperOrder> Place(PaperOrderViewModel input) => Use((s, d) => s.PlaceOrderAsync(input, default));
        var firstInput = Input();
        var first = await Place(firstInput);
        var portfolio = await Portfolio();
        Check(first.Status == PaperOrderStatus.Filled && portfolio.Account.Cash == 899857.50m, "1000 shares at 100 subtract gross plus 142.50 commission");
        Check(portfolio.Positions.Single().Position.AverageCost == 100.1425m, "buy commission is included in average cost");
        var replay = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Place(firstInput)));
        Check(replay.All(x => x.Id == first.Id) && (await Portfolio()).Trades.Count == 1, "concurrent replay of the same form cannot fill twice");
        market.Price = 110; market.High = 115; market.Low = 105;
        await Place(Input());
        portfolio = await Portfolio();
        Check(portfolio.Positions.Single().Position.AverageCost == 105.149625m && portfolio.Account.Cash == 789700.75m, "second acquisition weighted average and balance");
        market.Price = 120; market.High = 125; market.Low = 115;
        var sale = await Place(Input(PaperOrderSide.Sell, 500));
        portfolio = await Portfolio();
        var position = portfolio.Positions.Single().Position;
        Check(position.Quantity == 1500 && position.AverageCost == 105.149625m, "partial sale preserves average cost and reduces shares");
        Check(portfolio.Account.RealizedProfitLoss == 7159.6875m && position.RealizedProfitLoss == 7159.6875m, "realized profit subtracts commission tax and allocated cost");
        Check(portfolio.UnrealizedProfitLoss == 22275.5625m && portfolio.TotalAssets == 1029435.25m, "unrealized profit and total assets reconcile");
        Check(portfolio.Trades.Single(x => x.OrderId == sale.Id).TransactionTax == 180m, "stock sell tax is 0.3 percent simulation parameter");
        Check((await Place(Input(quantity:100000000))).RejectReason == "可用資金不足", "insufficient cash rejects without negative balance");
        Check((await Place(Input(PaperOrderSide.Sell, 2000))).RejectReason == "持有股數不足", "naked short rejects without negative holdings");
        foreach (var quantity in new[] { 0, -1 })
        {
            try { await Place(Input(quantity:quantity)); throw new Exception("Validation did not fail"); }
            catch (ValidationException) { Check(true, "invalid quantity " + quantity); }
        }
        foreach (var bad in new[] { Input(type:PaperOrderType.Limit), Input(limit:10), Input(side:(PaperOrderSide)99), Input(type:PaperOrderType.Limit,limit:-1) })
        {
            try { await Place(bad); throw new Exception("Validation did not fail"); }
            catch (ValidationException) { Check(true, "server validates enum and market/limit form invariants"); }
        }
        Check((await Place(Input(symbol:"9999"))).Status == PaperOrderStatus.Rejected, "unknown symbol rejected");
        var limitBuy = await Place(Input(quantity:1, type:PaperOrderType.Limit, limit:100));
        Check(limitBuy.Status == PaperOrderStatus.Pending, "new limit waits even when a historical daily bar might cross it");
        Check(await Use((s,d) => s.ExecutePendingOrdersAsync(default)) == 0, "submission-day and earlier OHLC cannot fill a new limit");
        async Task Eligible(long id) => await Use(async (s, d) => {
            var order = await d.PaperOrders.SingleAsync(x => x.Id == id);
            // Fixture simulates an order created before the quote day, without changing the host clock.
            order.CreatedAt = DateTimeOffset.UtcNow.AddDays(-3);
            order.SubmittedTradeDate = market.Date.AddDays(-1); order.EligibleFromTradeDate = market.Date;
            await d.SaveChangesAsync(); return true;
        });
        await Eligible(limitBuy.Id);
        market.Low = 101;
        Check(await Use((s,d) => s.ExecutePendingOrdersAsync(default)) == 0, "limit buy untouched stays pending");
        market.Low = 99;
        Check(await Use((s,d) => s.ExecutePendingOrdersAsync(default)) == 0, "same completed bar is never re-evaluated after revision");
        // Clear last evaluation ONLY in fixture to emulate a new eligible daily bar.
        async Task NextEvaluation(long id) => await Use(async (s,d) => { var o=await d.PaperOrders.SingleAsync(x=>x.Id==id);o.LastEvaluatedTradeDate=null;await d.SaveChangesAsync();return true; });
        await NextEvaluation(limitBuy.Id);
        var fills = await Task.WhenAll(Enumerable.Range(0,4).Select(_ => Use((s,d) => s.ExecutePendingOrdersAsync(default))));
        Check(fills.Sum() == 1 && (await Portfolio()).Trades.Count(x => x.OrderId == limitBuy.Id) == 1, "touched limit buy fills once under concurrent refresh");
        Check((await Portfolio()).Orders.Single(x=>x.Id==limitBuy.Id).FilledPrice == 100, "limit executes at submitted limit price");
        var limitSell = await Place(Input(PaperOrderSide.Sell, 1, PaperOrderType.Limit, 130));
        await Eligible(limitSell.Id);
        Check(await Use((s,d) => s.ExecutePendingOrdersAsync(default)) == 0, "limit sell untouched stays pending");
        market.High = 131; await NextEvaluation(limitSell.Id);
        Check(await Use((s,d) => s.ExecutePendingOrdersAsync(default)) == 1, "limit sell touched fills");
        var cancel = await Place(Input(quantity:1,type:PaperOrderType.Limit,limit:100));
        Check(await Use((s,d) => s.CancelOrderAsync(cancel.Id, default)), "pending can be cancelled");
        await Eligible(cancel.Id);
        Check(await Use((s,d) => s.ExecutePendingOrdersAsync(default)) == 0, "cancelled order never fills");
        Check(!await Use((s,d) => s.CancelOrderAsync(first.Id, default)), "filled order cannot be cancelled");
        market.Price=100;market.Low=95;market.High=105;
        await Place(Input(quantity:100,symbol:"006208"));
        var etfSale = await Place(Input(PaperOrderSide.Sell,100,symbol:"006208"));
        Check((await Portfolio()).Trades.Single(x=>x.OrderId==etfSale.Id).TransactionTax==10m, "ETF sell uses metadata and 0.1 percent simulated tax");
        Check(await Use((s,d)=>d.PaperPositions.AnyAsync(x=>x.StockId=="006208" && x.Quantity==0 && x.CostBasis==0 && x.AverageCost==0)), "zero position retained consistently");
        await Place(Input(quantity:1,symbol:"006208"));
        Check((await Portfolio()).Positions.Single(x=>x.Position.StockId=="006208").Position.AverageCost==120m, "reopened position starts fresh and respects minimum commission");
        market.Failure=true;
        portfolio=await Portfolio();
        Check(portfolio.HasIncompleteValuation && portfolio.TotalAssets==null && portfolio.Positions.All(x=>x.CurrentPrice==null), "API outage preserves holdings cash and history; incomplete totals are null");
        Check((await Place(Input(quantity:1))).Status==PaperOrderStatus.Rejected, "API outage rejects a new market order");
        market.Failure=false;
        var before=await Portfolio();
        try { await Use((s,d)=>s.PlaceOrderAsync(Input(quantity:1),default),new FailTradeSave()); throw new Exception("Expected transaction failure"); }
        catch(InvalidOperationException ex) when(ex.Message=="Injected trade failure") { }
        var after=await Portfolio();
        Check(before.Account.Cash==after.Account.Cash && before.Orders.Count==after.Orders.Count && before.Trades.Count==after.Trades.Count, "failure after order insert rolls back cash positions order and trade");
        await Use(async(s,d)=>{d.WorkLogs.Add(new(){Title="paper-test-sentinel",Content="must survive reset",WorkDate=new DateOnly(2026,9,23)});await d.SaveChangesAsync();return true;});
        Check(await Use((s,d)=>s.ResetAccountAsync(account.Generation,default)), "reset succeeds with current generation");
        portfolio=await Portfolio();
        Check(portfolio.Account.Cash==1000000 && portfolio.Account.RealizedProfitLoss==0 && portfolio.Trades.Count==0 && portfolio.Orders.Count==0 && portfolio.Positions.Count==0, "reset removes only paper ledgers and restores cash");
        Check(await Use((s,d)=>d.WorkLogs.AnyAsync(x=>x.Title=="paper-test-sentinel")), "WorkLogs survive reset");
        try {await Place(firstInput);throw new Exception("Stale form accepted");} catch(ValidationException){Check(true,"pre-reset form replay is rejected");}
        Check(!await Use((s,d)=>s.ResetAccountAsync(account.Generation,default)), "replayed reset cannot erase later activity");
        account=portfolio.Account;
        market.Price=600000;market.Low=590000;market.High=610000;
        var simultaneous=await Task.WhenAll(Place(Input(quantity:1)),Place(Input(quantity:1)));
        Check(simultaneous.Count(x=>x.Status==PaperOrderStatus.Filled)==1 && simultaneous.Count(x=>x.Status==PaperOrderStatus.Rejected)==1, "distinct concurrent buys cannot overspend");
        var sells=await Task.WhenAll(Place(Input(PaperOrderSide.Sell,1)),Place(Input(PaperOrderSide.Sell,1)));
        Check(sells.Count(x=>x.Status==PaperOrderStatus.Filled)==1 && sells.Count(x=>x.Status==PaperOrderStatus.Rejected)==1, "distinct concurrent sells cannot oversell");
        Check((await Portfolio()).Account.Cash>=0, "account never negative after concurrent operations");
        Console.WriteLine($"PASS: {checks} SQL-backed paper trading checks.");
    }
    sealed class FailTradeSave : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken ct=default)
        {
            if(eventData.Context!.ChangeTracker.Entries<PaperTrade>().Any(x=>x.State==EntityState.Added)) throw new InvalidOperationException("Injected trade failure");
            return ValueTask.FromResult(result);
        }
    }
}
