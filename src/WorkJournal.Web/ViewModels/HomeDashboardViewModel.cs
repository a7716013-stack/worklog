using WorkJournal.Web.Models;
namespace WorkJournal.Web.ViewModels;

public class HomeDashboardViewModel
{
    public string UserDisplayName { get; set; } = "";
    public DateOnly CurrentDate { get; set; }
    public bool IsAuthenticated { get; set; }
    public DashboardWorkSummary? WorkSummary { get; set; }
    public CalendarAggregation? CalendarSummary { get; set; }
    public MarketRadarResult? MarketSummary { get; set; }
    public DashboardRadarSummary? RadarSummary { get; set; }
    public DashboardInvestmentSummary? InvestmentSummary { get; set; }
}
public record DashboardWorkSummary(int Count, int Completed, IReadOnlyList<WorkLog> Items);
public record DashboardInvestmentSummary(int? WatchlistCount, int? TrackingCount, int? OpenPositionCount, int? PendingOrderCount);
public record DashboardRadarRow(string Symbol, string Name, int? Score, string Level);
public record DashboardIndustry(string Name, int Count);
public record DashboardRadarSummary(DateOnly? TradeDate, int Count, IReadOnlyList<DashboardRadarRow> Top, IReadOnlyList<DashboardIndustry> Industries);
