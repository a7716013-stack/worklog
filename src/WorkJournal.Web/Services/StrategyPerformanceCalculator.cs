using WorkJournal.Web.Models;
namespace WorkJournal.Web.Services;
public static class StrategyPerformanceCalculator
{
    public static StrategyHorizon[] Calculate(DateOnly signalDate, decimal signalClose, IEnumerable<DateOnly> calendar,
        IEnumerable<StrategyPrice> prices, DateOnly through, out decimal? nextOpen, out DateOnly? nextDate)
    {
        var days = calendar.Where(x => x > signalDate && x <= through).Distinct().Order().ToArray();
        var bars = prices.Where(x => x.Date > signalDate && x.Date <= through).DistinctBy(x => x.Date).ToDictionary(x => x.Date);
        nextDate = days.Length > 0 ? days[0] : null;
        nextOpen = nextDate.HasValue && bars.TryGetValue(nextDate.Value, out var first) && first.Valid && first.Volume > 0 ? first.Open : null;
        var entry = nextOpen;
        return new[] { 1, 3, 5, 10, 20 }.Select(n =>
        {
            if (days.Length < n) return new StrategyHorizon(n, null, "Pending", "Pending", null, null, null, null, null, null);
            var dates = days.Take(n).ToArray(); var end = bars.GetValueOrDefault(dates[^1]);
            var valid = end?.Valid == true && end.Volume > 0;
            var full = dates.All(x => bars.TryGetValue(x, out var b) && b.Valid && b.Volume > 0);
            // Large discontinuities need corporate-action review; do not fabricate adjusted returns.
            var previous = signalClose; var review = false;
            foreach (var date in dates) if (bars.TryGetValue(date, out var b) && b.Close > 0)
            { if (previous > 0 && Math.Abs(b.Close.Value / previous - 1) > .25m) review = true; previous = b.Close.Value; }
            decimal? Return(decimal? price, decimal? basis) => basis > 0 && price.HasValue && !review ? (price / basis - 1) * 100 : null;
            var high = full ? dates.Max(x => bars[x].High) : null; var low = full ? dates.Min(x => bars[x].Low) : null;
            var closeStatus = review ? "RequiresReview" : valid ? "Complete" : "Unavailable";
            var openStatus = review ? "RequiresReview" : valid && entry > 0 ? "Complete" : "Unavailable";
            return new StrategyHorizon(n, dates[^1], closeStatus, openStatus,
                valid ? Return(end!.Close, signalClose) : null, valid ? Return(end!.Close, entry) : null,
                Return(high, signalClose), Return(low, signalClose), Return(high, entry), Return(low, entry));
        }).ToArray();
    }
}
