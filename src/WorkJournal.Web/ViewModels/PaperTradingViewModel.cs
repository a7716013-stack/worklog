using WorkJournal.Web.Models;
namespace WorkJournal.Web.ViewModels;
public class PaperTradingViewModel
{
    public long? RecommendationId { get; set; }
    public MarketRadarRecommendation? Recommendation { get; set; }
    public PaperPortfolioViewModel Portfolio { get; set; } = new();
    public PaperOrderViewModel Order { get; set; } = new();
    public PaperTradingOptions Costs { get; set; } = new();
}
