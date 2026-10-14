using WorkJournal.Web.Models;
namespace WorkJournal.Web.Services;

public interface IStockUniverseService
{
    UniverseDecision Evaluate(StrategyInput data, ScanProfile profile);
}
public class StockUniverseService : IStockUniverseService
{
    public UniverseDecision Evaluate(StrategyInput d, ScanProfile p)
    {
        UniverseDecision No(string status, string reason) => new(status, reason, null, null);
        if (!d.Security.Cfi.StartsWith("ES", StringComparison.Ordinal)) return No("NotEligible", "UnsupportedSecurityType");
        if (!(d.Security.Market == "twse" && p.IncludeTWSE || d.Security.Market == "tpex" && p.IncludeTPEx)) return No("NotEligible", "MarketExcluded");
        if (!d.Security.NormalTrading) return No("NotEligible", "InactiveOrAbnormalSecurity");
        if (!d.Sessions.Contains(d.TradeDate)) return No("InsufficientData", "MissingTradingCalendar");
        var prices = d.Prices.Where(x => x.Date <= d.TradeDate).DistinctBy(x => x.Date).ToDictionary(x => x.Date);
        if (!prices.TryGetValue(d.TradeDate, out var current) || !current.Valid || current.Volume <= 0) return No("InsufficientData", "MissingPriceData");
        if (prices.Values.Count(x => x.Valid) < p.MinHistoryDays) return No("InsufficientData", "InsufficientHistory");
        var days = d.Sessions.Where(x => x < d.TradeDate).Order().TakeLast(20).ToArray();
        if (days.Length != 20 || days.Any(x => !prices.TryGetValue(x, out var b) || !b.Valid || b.TradingValue is null or < 0))
            return No("InsufficientData", "MissingLiquidityWindow");
        var value = days.Average(x => prices[x].TradingValue!.Value); var volume = days.Average(x => prices[x].Volume!.Value);
        return new(value < p.MinAvgTradingValue20D || p.MinAvgVolume20D.HasValue && volume < p.MinAvgVolume20D ? "NotEligible" : "Eligible",
            value < p.MinAvgTradingValue20D || p.MinAvgVolume20D.HasValue && volume < p.MinAvgVolume20D ? "LowLiquidity" : "", value, volume);
    }
}

public static class StrategyEngine
{
    public static readonly string[] Codes = ["RadarBenchmark", "TrendBreakout", "InstitutionalMomentum", "VolumeBreakout", "SwingCandidate"];
    public static string Name(string code) => code switch { "RadarBenchmark" => "Radar 基準", "TrendBreakout" => "趨勢突破", "InstitutionalMomentum" => "法人動能", "VolumeBreakout" => "量價突破", "SwingCandidate" => "波段候選", _ => code };
    public static string Level(string code) => code switch { "InstitutionalConfluence" => "法人共振", "ForeignMomentum" => "外資動能", "TrustMomentum" => "投信動能", _ => code };
    public static StrategyFacts Calculate(StrategyInput input, ScanProfile p, IStockUniverseService universe)
    {
        var f = new StrategyFacts { Input = input, Eligibility = universe.Evaluate(input, p) };
        var prices = input.Prices.Where(x => x.Date <= input.TradeDate).DistinctBy(x => x.Date).ToDictionary(x => x.Date);
        var sessions = input.Sessions.Where(x => x <= input.TradeDate).Distinct().Order().ToArray();
        StrategyPrice[] Window(int n, bool prior = false)
        {
            var dates = sessions.Where(x => !prior || x < input.TradeDate).TakeLast(n).ToArray();
            return dates.Length == n && dates.All(x => prices.TryGetValue(x, out var b) && b.Valid) ? dates.Select(x => prices[x]).ToArray() : [];
        }
        decimal? Ma(int n) { var w = Window(n); return w.Length == n ? w.Average(x => x.Close) : null; }
        decimal? High(int n) { var w = Window(n, true); return w.Length == n ? w.Max(x => x.High) : null; }
        decimal? Volume(int n) { var w = Window(n, true); return w.Length == n ? w.Average(x => x.Volume) : null; }
        f.MA5 = Ma(5); f.MA10 = Ma(10); f.MA20 = Ma(20); f.MA60 = Ma(60); f.MA120 = Ma(120);
        f.Previous20DHigh = High(20); f.Previous60DHigh = High(60); f.VolumeMA5 = Volume(5); f.VolumeMA20 = Volume(20);
        var today = prices.GetValueOrDefault(input.TradeDate);
        f.VolumeRatio20 = f.VolumeMA20 > 0 ? today?.Volume / f.VolumeMA20 : null;
        var pair = Window(2); f.ChangePercent = pair.Length == 2 ? (pair[1].Close / pair[0].Close - 1) * 100 : null;
        f.Technical = TechnicalIndicators.Calculate(sessions.Select(x => new DailyClose(x, prices.GetValueOrDefault(x)?.Close)));
        var flows = input.Flows.Where(x => x.Date <= input.TradeDate).DistinctBy(x => x.Date).ToDictionary(x => x.Date);
        foreach (var name in new[] { "Foreign", "Trust", "Dealer" })
        {
            decimal? Net(SwingFlow b) => name == "Foreign" ? b.Foreign : name == "Trust" ? b.Trust : b.Dealer;
            foreach (var n in new[] { 1, 3, 5, 10, 20 })
            {
                var dates = sessions.TakeLast(n).ToArray();
                var complete = dates.Length == n && dates.All(x => flows.TryGetValue(x, out var b) && Net(b).HasValue);
                decimal? net = complete ? dates.Sum(x => Net(flows[x])) : null;
                f.Institutional[$"{name}NetBuy{n}D"] = net;
                f.Institutional[$"{name}BuyDays{n}"] = complete ? dates.Count(x => Net(flows[x]) > 0) : null;
                var bars = Window(n); var volume = bars.Length == n ? bars.Sum(x => x.Volume) : null;
                f.Institutional[$"Volume{n}D"] = volume;
                f.Institutional[$"{name}BuyRatio{n}D"] = volume > 0 ? net / volume : null;
            }
        }
        if (today?.Valid == true)
        {
            var quote = new MarketQuote(new(input.Security.Symbol, input.Security.Name, input.Security.Market, false), input.TradeDate,
                today.Close!.Value, pair.Length == 2 ? today.Close - pair[0].Close : null, today.Volume!.Value);
            // V1 calculator is unchanged. Research completeness has its own explicit score version.
            f.Radar = MarketRadarScoreCalculator.Calculate(quote,
                new(input.Prices.Where(x => x.Date <= input.TradeDate).Select(x => x.ToBar()).ToArray(), input.Flows, input.RevenueYoY, input.RevenueMonth), input.Events);
            var parts = f.Radar.Parts.Select(x => x.Name == "重大資訊" && input.EventCoverageComplete && x.Points is null ? x with { Points = 0 } : x).ToArray();
            f.RadarScore = parts.All(x => x.Points.HasValue) && f.MA60.HasValue && f.VolumeRatio20.HasValue && f.Technical.Histogram.HasValue ? parts.Sum(x => x.Points) : null;
            if (f.Eligibility.Status == "Eligible" && f.RadarScore >= p.RecommendationMinScore && today.Close > f.MA20 && f.MA20 >= f.MA60)
                f.SwingLevel = f.RadarScore >= p.StrongRecommendationMinScore ? "強力推薦" : f.RadarScore >= p.RecommendationGradeScore ? "波段推薦" : "波段觀察";
        }
        return f;
    }
    public static StrategyDecision Evaluate(string code, StrategyFacts f, ScanProfile p)
    {
        StrategyDecision Result(string status, string reason, string level = "", params string[] missing) => new(code, status, level, reason, missing);
        if (f.Eligibility.Status != "Eligible") return Result(f.Eligibility.Status == "NotEligible" ? "NotEligible" : "InsufficientData", f.Eligibility.Reason, "", f.Eligibility.Reason);
        var close = f.Input.Prices.First(x => x.Date == f.Input.TradeDate).Close;
        if (code is "RadarBenchmark" or "SwingCandidate")
        {
            if (!f.RadarScore.HasValue) return Result("InsufficientData", "Radar 評分必要資料不完整", "", "RadarScore");
            var hit = code == "RadarBenchmark" ? f.RadarScore >= p.BenchmarkMinScore : f.SwingLevel.Length > 0;
            return Result(hit ? "Matched" : "NotMatched", code == "RadarBenchmark" ? $"完整 Radar 評分 {f.RadarScore}，門檻 {p.BenchmarkMinScore}。" : $"Radar {f.RadarScore}；收盤 {close:0.00}、MA20 {f.MA20:0.00}、MA60 {f.MA60:0.00}。", f.SwingLevel);
        }
        if (f.MA5 is null || f.MA20 is null) return Result("InsufficientData", "均線窗口不完整", "", "MA5/MA20");
        if (code == "InstitutionalMomentum")
        {
            var v = f.Institutional;
            var required = new[] { "ForeignNetBuy5D", "ForeignBuyDays5", "TrustNetBuy5D", "TrustBuyDays5" };
            if (required.Any(x => !v.GetValueOrDefault(x).HasValue)) return Result("InsufficientData", "外資或投信 5 日資料不完整", "", required.Where(x => !v.GetValueOrDefault(x).HasValue).ToArray());
            var price = close > f.MA20 && f.MA5 >= f.MA20;
            var foreign = v[required[0]] > 0 && v[required[1]] >= p.ForeignBuyDays;
            var trust = v[required[2]] > 0 || v[required[3]] >= p.TrustBuyDays;
            var level = foreign && trust ? "InstitutionalConfluence" : foreign ? "ForeignMomentum" : trust ? "TrustMomentum" : "";
            return Result(price && (foreign || trust) ? "Matched" : "NotMatched",
                $"外資近 5 日 {v[required[1]]:0} 日買超、淨額 {v[required[0]]:N0} 股；投信 {v[required[3]]:0} 日、淨額 {v[required[2]]:N0} 股；價格確認 {(price ? "符合" : "未符合")}。", level);
        }
        if (f.Previous20DHigh is null || f.VolumeRatio20 is null || f.ChangePercent is null || code == "TrendBreakout" && f.MA60 is null)
            return Result("InsufficientData", "突破／成交量／價格窗口不完整", "", "PriceVolumeWindow");
        var matched = code == "TrendBreakout"
            ? close > f.MA20 && f.MA5 > f.MA20 && f.MA20 > f.MA60 && close > f.Previous20DHigh && f.VolumeRatio20 >= p.TrendVolumeRatio
            : close > f.Previous20DHigh && f.VolumeRatio20 >= p.BreakoutVolumeRatio && f.ChangePercent > 0 && close > f.MA20;
        return Result(matched ? "Matched" : "NotMatched", $"收盤 {close:0.00}；前 20 日高點 {f.Previous20DHigh:0.00}；20 日量比 {f.VolumeRatio20:0.00}；MA20 {f.MA20:0.00}、MA60 {f.MA60:0.00}。");
    }
}
