using WorkJournal.Web.Models;
namespace WorkJournal.Web.Services;

public static class TechnicalIndicators
{
    public static TechnicalData Calculate(IEnumerable<DailyClose> rows)
    {
        var ordered = rows.GroupBy(x => x.Date).Select(x => x.Last()).OrderBy(x => x.Date).ToArray();
        // Never bridge missing/invalid closes by silently dropping trading sessions.
        var lastInvalid = Array.FindLastIndex(ordered, x => x.Close is null or <= 0);
        var valid = ordered.Skip(lastInvalid + 1).ToArray();
        var close = valid.Select(x => x.Close!.Value).ToArray();
        decimal? Ma(int n) => close.Length >= n ? close.TakeLast(n).Average() : null;
        decimal? rsi = null;
        if (close.Length >= 15)
        {
            decimal gain = 0, loss = 0;
            for (int i = 1; i <= 14; i++) { var d = close[i] - close[i - 1]; gain += Math.Max(d, 0); loss += Math.Max(-d, 0); }
            gain /= 14; loss /= 14;
            for (int i = 15; i < close.Length; i++)
            {
                var d = close[i] - close[i - 1];
                gain = (gain * 13 + Math.Max(d, 0)) / 14;
                loss = (loss * 13 + Math.Max(-d, 0)) / 14;
            }
            rsi = loss == 0 ? (gain == 0 ? 50 : 100) : 100 - 100 / (1 + gain / loss);
        }
        var fast = Ema(close, 12); var slow = Ema(close, 26);
        var differences = Enumerable.Range(0, close.Length).Where(i => slow[i].HasValue)
            .Select(i => fast[i]!.Value - slow[i]!.Value).ToArray();
        var signals = Ema(differences, 9);
        decimal? macd = differences.Length > 0 ? differences[^1] : null;
        decimal? signal = signals.Length > 0 ? signals[^1] : null;
        return new TechnicalData {
            Date = valid.Length > 0 ? valid[^1].Date : null, Samples = close.Length,
            MA5 = Ma(5), MA20 = Ma(20), MA60 = Ma(60), RSI14 = rsi,
            MACD = macd, Signal = signal, Histogram = macd - signal
        };
    }
    private static decimal?[] Ema(decimal[] values, int period)
    {
        var result = new decimal?[values.Length];
        if (values.Length < period) return result;
        var ema = values.Take(period).Average();
        result[period - 1] = ema;
        var alpha = 2m / (period + 1);
        for (int i = period; i < values.Length; i++) { ema += alpha * (values[i] - ema); result[i] = ema; }
        return result;
    }
}
