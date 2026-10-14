using WorkJournal.Web.Models;
namespace WorkJournal.Web.ViewModels;
public class StrategyCenterViewModel
{
    public DateOnly? Date { get; set; }
    public DateOnly? From { get; set; }
    public string Tab { get; set; } = "overview";
    public string Market { get; set; } = "all";
    public string Strategy { get; set; } = "";
    public string Symbol { get; set; } = "";
    public string Basis { get; set; } = "open";
    public string Group { get; set; } = "strategy";
    public int Horizon { get; set; } = 20;
    public int? ProfileId { get; set; }
    public int Page { get; set; } = 1;
    public int TotalSignals { get; set; }
    public List<ScanProfile> Profiles { get; set; } = [];
    public List<StrategyScanRun> Runs { get; set; } = [];
    public List<StrategySignal> Signals { get; set; } = [];
    public List<StrategyDailyStock> Radar { get; set; } = [];
    public List<StrategyStatistic> Statistics { get; set; } = [];
    public List<(string Reason, int Count)> Skips { get; set; } = [];
}
public record StrategyStatistic(string Label, int Episodes, int Valid, decimal? WinRate, decimal? Mean, decimal? Median, decimal? Mfe, decimal? Mae);
