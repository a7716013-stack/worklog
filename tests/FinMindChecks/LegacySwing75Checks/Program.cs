using WorkJournal.Web.Models;
using WorkJournal.Web.Services;

// This is a frozen legacy research threshold, NOT the V2 trading threshold.
// Compile the REAL production backtester with a calculator test double. This
// isolates its threshold policy, since the real calculator emits multiples of 5.
// Real calculator behavior is covered separately by FinMindChecks/SwingChecks.
var day = new DateOnly(2025, 1, 1);
var bars = new[] {
    new SwingBar(day, 100, 101, 99, 100, 1000),
    new SwingBar(day.AddDays(1), 100, 111, 99, 110, 1000)
};
var history = new SwingHistory(new("2330", "Threshold fixture", "twse", false), bars, [], "");
void Check(bool condition, string name) { if (!condition) throw new Exception(name); Console.WriteLine("PASS: " + name); }
SwingCalculator.FirstScore = 74;
var rejected = SwingBacktester.Run(history, day, 0, 0);
Check(rejected.OpenPositions == 0 && rejected.Trades.Count == 0 && rejected.TotalReturn == 0,
    "LegacySwing75_74_IsNotEligible");
SwingCalculator.FirstScore = 75;
var accepted = SwingBacktester.Run(history, day, 0, 0);
Check(accepted.OpenPositions == 1 && accepted.TotalReturn == 10 && accepted.Trades.Count == 0,
    "LegacySwing75_75_IsEligible_NextOpenFill");
Check(SwingBacktester.Run(history with { Bars = bars.Take(1).ToArray() }, day, 0, 0).OpenPositions == 0,
    "LegacySwing75_NoSameDayFill");
SwingCalculator.FirstScore = null;
Check(SwingBacktester.Run(history, day, 0, 0).OpenPositions == 0,
    "LegacySwing75_Missing_IsNotEligible");

namespace WorkJournal.Web.Services
{
    public static class SwingCalculator
    {
        public static int? FirstScore { get; set; }
        public static SwingBar[] Normalize(IEnumerable<SwingBar> bars) => bars.OrderBy(x => x.Date).ToArray();
        public static SwingSnapshot Calculate(IReadOnlyList<SwingBar> bars, IReadOnlyList<SwingFlow> flows)
            => new() { Score = bars.Count == 1 ? FirstScore : 0, MA20 = 90 };
    }
}
