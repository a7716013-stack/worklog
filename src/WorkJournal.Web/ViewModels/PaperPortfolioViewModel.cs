using WorkJournal.Web.Models;
namespace WorkJournal.Web.ViewModels;
public record PaperPositionValuation(PaperPosition Position, StockQuote? Quote)
{
    public decimal? CurrentPrice => Quote?.CurrentPrice > 0 && Quote.TradeDate != null ? Quote.CurrentPrice : null;
    public decimal? MarketValue => CurrentPrice * Position.Quantity;
    public decimal? UnrealizedProfitLoss => MarketValue - Position.CostBasis;
    public decimal? ReturnPercent => Position.CostBasis > 0 ? UnrealizedProfitLoss / Position.CostBasis * 100m : null;
}
public class PaperPortfolioViewModel
{
    public PaperTradingAccount Account { get; set; } = new();
    public List<PaperPositionValuation> Positions { get; set; } = [];
    public List<PaperOrder> Orders { get; set; } = [];
    public List<PaperTrade> Trades { get; set; } = [];
    public bool HasIncompleteValuation => Positions.Any(x => x.CurrentPrice == null);
    public decimal TotalMarketValue => Positions.Sum(x => x.MarketValue ?? 0m);
    public decimal? UnrealizedProfitLoss => HasIncompleteValuation ? null : Positions.Sum(x => x.UnrealizedProfitLoss ?? 0m);
    public decimal? TotalAssets => HasIncompleteValuation ? null : Account.Cash + TotalMarketValue;
    public decimal? TotalProfitLoss => TotalAssets - Account.InitialCash;
    public decimal? TotalReturnPercent => Account.InitialCash > 0 ? TotalProfitLoss / Account.InitialCash * 100m : null;
}
