using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using WorkJournal.Web.Data;
using WorkJournal.Web.Models;
using WorkJournal.Web.ViewModels;
namespace WorkJournal.Web.Services;

public class PaperTradingService(JournalDbContext db, FinMindStockService stocks, IOptions<PaperTradingOptions> options) : IPaperTradingService
{
    private readonly PaperTradingOptions costs = options.Value;
    private readonly Dictionary<string, StockLookup> quotes = new(StringComparer.Ordinal);

    // SQL transaction-owned lock serializes account creation, fill, cancel and reset across processes.
    // All market requests happen BEFORE this lock. Retry clears tracked entities and re-reads SQL.
    private Task<T> Atomic<T>(Func<Task<T>> action, CancellationToken ct) => db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
    {
        db.ChangeTracker.Clear();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlRawAsync("""
            DECLARE @result int;
            EXEC @result = sys.sp_getapplock @Resource = N'WorkJournal.PaperTrading.Default',
                @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 15000;
            IF @result < 0 THROW 51000, 'Paper trading account is busy.', 1;
            """, ct);
        var result = await action();
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return result;
    });

    private async Task<PaperTradingAccount> Account(CancellationToken ct)
    {
        var account = await db.PaperTradingAccounts.SingleOrDefaultAsync(x => x.Id == 1, ct);
        if (account != null) return account;
        account = new() { InitialCash = costs.InitialCash, Cash = costs.InitialCash };
        db.PaperTradingAccounts.Add(account);
        await db.SaveChangesAsync(ct);
        return account;
    }

    public Task<PaperTradingAccount> GetAccountAsync(CancellationToken ct) => Atomic(() => Account(ct), ct);
    private async Task<StockLookup> Quote(string symbol, CancellationToken ct)
    {
        if (!quotes.TryGetValue(symbol, out var value))
            quotes[symbol] = value = await stocks.GetPaperQuoteAsync(symbol, ct);
        return value;
    }

    public async Task<PaperPortfolioViewModel> GetPortfolioAsync(CancellationToken ct)
    {
        var snapshot = await Atomic(async () => new {
            Account = await Account(ct),
            Positions = await db.PaperPositions.AsNoTracking().Where(x => x.AccountId == 1 && x.Quantity > 0).OrderBy(x => x.StockId).ToListAsync(ct),
            Orders = await GetOrdersAsync(ct), Trades = await GetTradesAsync(ct)
        }, ct);
        var result = new PaperPortfolioViewModel { Account = snapshot.Account, Orders = snapshot.Orders, Trades = snapshot.Trades };
        // Sequential bounded requests and request-scoped de-duplication, on top of shared FinMind cache.
        foreach (var position in snapshot.Positions)
            result.Positions.Add(new(position, (await Quote(position.StockId, ct)).Quote));
        return result;
    }

    public Task<List<PaperOrder>> GetOrdersAsync(CancellationToken ct) => db.PaperOrders.AsNoTracking()
        .Where(x => x.AccountId == 1).OrderByDescending(x => x.Id).ToListAsync(ct);
    public Task<List<PaperTrade>> GetTradesAsync(CancellationToken ct) => db.PaperTrades.AsNoTracking()
        .Where(x => x.AccountId == 1).OrderByDescending(x => x.Id).ToListAsync(ct);

    public async Task<PaperOrder> PlaceOrderAsync(PaperOrderViewModel input, CancellationToken ct)
    {
        Validator.ValidateObject(input, new ValidationContext(input), true);
        var lookup = await Quote(input.StockId, ct);
        return await Atomic(async () =>
        {
            var account = await Account(ct);
            if (account.Generation != input.AccountGeneration) throw new ValidationException("帳戶已重設，請重新整理後再建立委託。");
            var existing = await db.PaperOrders.SingleOrDefaultAsync(x => x.AccountId == 1 && x.ClientRequestId == input.ClientRequestId, ct);
            if (existing != null) return existing; // Form replay and ambiguous commit retries return the same order.
            var quote = lookup.Quote;
            var order = new PaperOrder {
                AccountId = 1, ClientRequestId = input.ClientRequestId, StockId = input.StockId,
                StockName = quote?.Name ?? input.StockId, IsEtf = quote?.IsEtf ?? false,
                Side = input.Side, OrderType = input.OrderType, Quantity = input.Quantity,
                LimitPrice = input.LimitPrice, SubmittedTradeDate = quote?.TradeDate
            };
            db.PaperOrders.Add(order);
            if (quote?.TradeDate == null || quote.CurrentPrice is not > 0)
                Reject(order, lookup.Message);
            else
            {
                var today = DateOnly.FromDateTime(order.CreatedAt.ToOffset(TimeSpan.FromHours(8)).DateTime);
                // Conservatively never use the submission calendar day's OHLC, even before its close.
                order.EligibleFromTradeDate = (quote.TradeDate > today ? quote.TradeDate.Value : today).AddDays(1);
                var position = await db.PaperPositions.SingleOrDefaultAsync(x => x.AccountId == 1 && x.StockId == order.StockId, ct);
                var referencePrice = order.OrderType == PaperOrderType.Limit ? order.LimitPrice!.Value : quote.CurrentPrice.Value;
                var reason = ValidateResources(account, position, order, referencePrice);
                if (reason != null) Reject(order, reason);
                else if (order.OrderType == PaperOrderType.Market)
                {
                    await db.SaveChangesAsync(ct); // Obtain order ID inside the same transaction.
                    await Fill(account, order, quote, ct);
                }
            }
            return order;
        }, ct);
    }

    private string? ValidateResources(PaperTradingAccount account, PaperPosition? position, PaperOrder order, decimal price)
    {
        if (price <= 0 || price > 1000000) return "無有效成交價格";
        if (order.Side == PaperOrderSide.Buy)
        {
            if ((long)(position?.Quantity ?? 0) + order.Quantity > int.MaxValue) return "持股數量超過上限";
            var gross = PaperTradingCalculator.Money(price * order.Quantity);
            if (account.Cash < gross + PaperTradingCalculator.Commission(gross, costs)) return "可用資金不足";
        }
        else if (position == null || position.Quantity < order.Quantity) return "持有股數不足";
        return null;
    }

    private static void Reject(PaperOrder order, string reason)
    { order.Status = PaperOrderStatus.Rejected; order.RejectReason = reason; }

    private async Task<bool> Fill(PaperTradingAccount account, PaperOrder order, StockQuote quote, CancellationToken ct)
    {
        var price = PaperTradingCalculator.FillPrice(order, quote);
        if (price is null) return false;
        var position = await db.PaperPositions.SingleOrDefaultAsync(x => x.AccountId == 1 && x.StockId == order.StockId, ct);
        var reason = ValidateResources(account, position, order, price.Value);
        if (reason != null) { Reject(order, reason); return false; }
        var gross = PaperTradingCalculator.Money(price.Value * order.Quantity);
        var commission = PaperTradingCalculator.Commission(gross, costs);
        var tax = order.Side == PaperOrderSide.Sell ? PaperTradingCalculator.Tax(gross, order.IsEtf, costs) : 0m;
        var cashFlow = order.Side == PaperOrderSide.Buy ? -(gross + commission) : gross - commission - tax;
        if (account.Cash + cashFlow < 0) { Reject(order, "可用資金不足以支付交易費用"); return false; }
        var now = DateTimeOffset.UtcNow;
        decimal realized = 0;
        if (order.Side == PaperOrderSide.Buy)
        {
            if (position == null)
            {
                position = new() { AccountId = 1, StockId = order.StockId, StockName = order.StockName };
                db.PaperPositions.Add(position);
            }
            position.CostBasis += gross + commission;
            position.Quantity += order.Quantity;
            position.AverageCost = decimal.Round(position.CostBasis / position.Quantity, 10, MidpointRounding.AwayFromZero);
        }
        else
        {
            var removedCost = PaperTradingCalculator.RemoveCost(position!, order.Quantity);
            realized = cashFlow - removedCost;
            position!.CostBasis -= removedCost;
            position.Quantity -= order.Quantity;
            position.RealizedProfitLoss += realized;
            // Retain closed positions for cumulative realized P/L. Reopening starts with zero cost.
            if (position.Quantity == 0) { position.AverageCost = 0; position.CostBasis = 0; }
        }
        position.UpdatedAt = now;
        account.Cash += cashFlow;
        account.RealizedProfitLoss += realized;
        account.UpdatedAt = now;
        order.Status = PaperOrderStatus.Filled;
        order.FilledAt = now;
        order.FilledPrice = price;
        order.FilledTradeDate = quote.TradeDate;
        db.PaperTrades.Add(new() {
            AccountId = 1, OrderId = order.Id, StockId = order.StockId, StockName = order.StockName,
            Side = order.Side, Quantity = order.Quantity, Price = price.Value, GrossAmount = gross,
            Commission = commission, TransactionTax = tax, NetCashAmount = cashFlow,
            RealizedProfitLoss = realized, QuoteTradeDate = quote.TradeDate!.Value, ExecutedAt = now
        });
        return true;
    }

    public Task<bool> CancelOrderAsync(long id, CancellationToken ct) => Atomic(async () =>
    {
        var order = await db.PaperOrders.SingleOrDefaultAsync(x => x.Id == id && x.AccountId == 1, ct);
        if (order?.Status != PaperOrderStatus.Pending) return false;
        order.Status = PaperOrderStatus.Cancelled;
        order.CancelledAt = DateTimeOffset.UtcNow;
        return true;
    }, ct);

    public async Task<int> ExecutePendingOrdersAsync(CancellationToken ct)
    {
        var pending = await db.PaperOrders.AsNoTracking().Where(x => x.AccountId == 1 && x.Status == PaperOrderStatus.Pending)
            .OrderBy(x => x.Id).Select(x => new { x.Id, x.StockId }).ToListAsync(ct);
        var count = 0;
        foreach (var item in pending)
        {
            var quote = (await Quote(item.StockId, ct)).Quote;
            if (quote?.TradeDate == null) continue; // API outage preserves pending orders.
            if (await Atomic(async () =>
            {
                var account = await Account(ct);
                var order = await db.PaperOrders.SingleOrDefaultAsync(x => x.AccountId == 1 && x.Id == item.Id, ct);
                if (order?.Status != PaperOrderStatus.Pending || order.EligibleFromTradeDate == null ||
                    quote.TradeDate < order.EligibleFromTradeDate || quote.TradeDate <= order.LastEvaluatedTradeDate) return false;
                var filled = await Fill(account, order, quote, ct);
                if (quote.LowPrice > 0 && quote.HighPrice >= quote.LowPrice) order.LastEvaluatedTradeDate = quote.TradeDate;
                return filled;
            }, ct)) count++;
        }
        return count;
    }

    public Task<bool> ResetAccountAsync(Guid generation, CancellationToken ct) => Atomic(async () =>
    {
        var account = await Account(ct);
        if (account.Generation != generation) return false;
        await db.PaperTrades.Where(x => x.AccountId == 1).ExecuteDeleteAsync(ct);
        await db.PaperOrders.Where(x => x.AccountId == 1).ExecuteDeleteAsync(ct);
        await db.PaperPositions.Where(x => x.AccountId == 1).ExecuteDeleteAsync(ct);
        account.Cash = account.InitialCash;
        account.RealizedProfitLoss = 0;
        account.Generation = Guid.NewGuid();
        account.UpdatedAt = DateTimeOffset.UtcNow;
        return true;
    }, ct);
}
