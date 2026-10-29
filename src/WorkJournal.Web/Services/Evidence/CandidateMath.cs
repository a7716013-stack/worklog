using WorkJournal.Web.Models.Evidence;
namespace WorkJournal.Web.Services.Evidence;

public static class CandidateMath
{
    public static decimal Clamp01(decimal x) => Math.Clamp(x, 0m, 1m);
    public static decimal Linear(decimal x, LinearBounds bounds)
    {
        if (bounds.Upper <= bounds.Lower) throw new ArgumentException("Upper bound must exceed lower bound.");
        // Compare before subtracting to avoid overflow for outliers outside the bounds.
        if (x <= bounds.Lower) return 0;
        if (x >= bounds.Upper) return 1;
        return (x - bounds.Lower) / (bounds.Upper - bounds.Lower);
    }
    public static decimal InverseLinear(decimal x, LinearBounds bounds) => 1 - Linear(x, bounds);
    public static decimal Percentile(decimal x, IReadOnlyList<decimal> population)
    {
        if (population.Count == 0) throw new ArgumentException("Empty historical population.");
        return (population.Count(v => v < x) + .5m * population.Count(v => v == x)) / population.Count;
    }
    public static decimal Piecewise(decimal x, IReadOnlyList<Anchor> anchors)
    {
        if (anchors.Count < 2 || anchors.Any(a => a.Y is < 0 or > 1) ||
            anchors.Zip(anchors.Skip(1)).Any(a => a.First.X >= a.Second.X))
            throw new ArgumentException("Ordered unique anchors in [0,1] are required.");
        if (x <= anchors[0].X) return anchors[0].Y;
        for (int i = 1; i < anchors.Count; i++)
            if (x <= anchors[i].X) return anchors[i-1].Y + (anchors[i].Y-anchors[i-1].Y) *
                (x-anchors[i-1].X)/(anchors[i].X-anchors[i-1].X);
        return anchors[^1].Y;
    }
    // Decimal series with fixed iteration limits, avoiding platform-dependent binary libm results.
    public static decimal Log(decimal x)
    {
        if (x <= 0) throw new ArgumentOutOfRangeException(nameof(x));
        const decimal ln2 = .6931471805599453094172321215m;
        int k = 0;
        while (x > 2) { x /= 2; k++; }
        while (x < 1) { x *= 2; k--; }
        decimal z = (x-1)/(x+1), power = z, sum = 0;
        for (int n = 1; n <= 199; n += 2) { sum += power/n; power *= z*z; }
        return 2*sum+k*ln2;
    }
    public static decimal Sqrt(decimal x)
    {
        if (x < 0) throw new ArgumentOutOfRangeException(nameof(x));
        if (x == 0) return 0;
        decimal r = x >= 1 ? x : 1;
        for (int i = 0; i < 200; i++) { var next = r/2 + x/r/2; if (next == r) return r; r = next; }
        return r;
    }
    public static decimal Median(IEnumerable<decimal> input)
    {
        var a = input.Order().ToArray();
        if (a.Length == 0) throw new ArgumentException("Empty median.");
        return a.Length % 2 == 1 ? a[a.Length/2] : a[a.Length/2-1]/2+a[a.Length/2]/2;
    }
}
