using WorkJournal.Web.Models;
namespace WorkJournal.Web.Services;
public static class PaperTradingCalculator
{
    public static decimal Money(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero);
    public static decimal Commission(decimal gross, PaperTradingOptions options) =>
        Money(Math.Max(options.MinimumCommission, gross * options.CommissionRate * options.CommissionDiscount));
    public static decimal Tax(decimal gross, bool isEtf, PaperTradingOptions options) =>
        Money(gross * (isEtf ? options.EtfSellTaxRate : options.StockSellTaxRate));
    public static decimal? FillPrice(PaperOrder order, StockQuote quote)
    {
        if (order.Status != PaperOrderStatus.Pending || quote.TradeDate == null || quote.CurrentPrice is not > 0) return null;
        if (order.OrderType == PaperOrderType.Market) return quote.CurrentPrice;
        if (order.EligibleFromTradeDate == null || quote.TradeDate < order.EligibleFromTradeDate ||
            quote.TradeDate <= order.SubmittedTradeDate || quote.TradeDate <= order.LastEvaluatedTradeDate) return null;
        // OHLC-only approximation: use the submitted limit, never infer intraday order.
        if (quote.LowPrice is not > 0 || quote.HighPrice is not > 0 || quote.LowPrice > quote.HighPrice) return null;
        return order.Side == PaperOrderSide.Buy
            ? (quote.LowPrice <= order.LimitPrice ? order.LimitPrice : null)
            : (quote.HighPrice >= order.LimitPrice ? order.LimitPrice : null);
    }
    public static decimal RemoveCost(PaperPosition position, int quantity) => quantity == position.Quantity
        ? position.CostBasis : decimal.Round(position.AverageCost * quantity, 10, MidpointRounding.AwayFromZero);
}
