using WorkJournal.Web.Models;
namespace WorkJournal.Web.ViewModels;
public class RadarTrackingFilter
{
    public DateOnly? From { get; set; }
    public DateOnly? To { get; set; }
    public string Market { get; set; }="all";
    public string Category { get; set; }="all";
    public string? Industry { get; set; }="";
    public string? Symbol { get; set; }="";
    public string Result { get; set; }="all";
    public string Basis { get; set; }="close";
    public int? ScoreMin { get; set; }
    public int? ScoreMax { get; set; }
    public bool Mine { get; set; }
    public int Page { get; set; }=1;
    public int PageSize { get; set; }=20;
}
public record RadarTrackingItem(MarketRadarRecommendation Recommendation,MarketRadarPerformance? Performance,MarketRadarPersonalTracking? Personal);
public record RadarTrackingViewModel(RadarTrackingFilter Filter,List<RadarTrackingItem> Items,int TotalCount)
{public int TotalPages=>(int)Math.Ceiling((double)TotalCount/Filter.PageSize);}
public record RadarStatisticsRow(string Group,int Count,int Matured5,int Matured20,decimal? Win5,decimal? Win20,decimal? Average5,decimal? Average20,decimal? Median5,decimal? Median20,decimal? Mfe20,decimal? Mae20,decimal? AverageScore=null);
public record RadarValidationRow(string Group,int Count,decimal?[] Win,decimal?[] Average,int[] Matured,decimal? Median5=null,decimal? Median20=null,decimal? Mfe20=null,decimal? Mae20=null);
public record RadarComponentRow(string Name,string Group,int Count,decimal? Win5,decimal? Win20,decimal? Average5,decimal? Average20,int Matured5,int Matured20);
public record RadarPerformanceViewModel(RadarTrackingFilter Filter,List<RadarStatisticsRow> Groups,List<RadarStatisticsRow> Industries,
    List<RadarValidationRow> Validation,List<RadarComponentRow> Components,decimal? Correlation5,decimal? Correlation20)
{ public List<RadarSwingTarget> SwingTargets { get; init; } = []; }
public record RadarSwingTarget(string Symbol, string Name);
public record RadarPaperComparison(long RecommendationId,string Symbol,DateOnly TradeDate,decimal? Radar20,decimal? Open20,int Orders,int Filled,int Deleted,decimal RealizedProfit,decimal? RealizedReturn,decimal? Difference);
