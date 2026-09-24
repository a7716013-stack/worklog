using WorkJournal.Web.Models;
namespace WorkJournal.Web.ViewModels;
public class PaperTradingViewModel
{
    public PaperPortfolioViewModel Portfolio { get; set; } = new();
    public PaperOrderViewModel Order { get; set; } = new();
    public PaperTradingOptions Costs { get; set; } = new();
}
