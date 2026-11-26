namespace BuyWindow.Core;

/// <summary>
/// One Kalshi contract on a number (CPI, unemployment, the S&amp;P 500…). It pays out if the value lands
/// in [Low, High); a null bound is open-ended. "Above 3.5" is Low 3.5, High null.
/// </summary>
public sealed record RangeContract(double? Low, double? High, double? Bid, double? Ask, double? Last)
{
    public double? Price => FedOdds.Price(new FedContract("", Bid, Ask, Last));
}

/// <summary>Odds, 0-1, that a release prints hotter than, in line with, or cooler than the consensus.</summary>
public sealed record SurpriseOdds(double Hotter, double InLine, double Cooler)
{
    /// <summary>One side has to beat the other by this much to call a lean.</summary>
    public const double LeanMargin = 0.15;

    public string Lean => Hotter - Cooler >= LeanMargin ? "hot" : Cooler - Hotter >= LeanMargin ? "cool" : "balanced";
}

/// <summary>
/// Turns Kalshi's number markets into the market's median guess. Like the Fed odds, this is context
/// beside the buy score, never part of it.
/// </summary>
public static class MacroOdds
{
    /// <summary>
    /// Works for both shapes Kalshi uses: a ladder of "above X" contracts (each price is P(value &gt; X))
    /// and a set of buckets that together cover every outcome (each price is P(value in bucket)).
    /// </summary>
    public static double? Median(IEnumerable<RangeContract> contracts)
    {
        var priced = contracts.Where(c => c.Price is not null).ToList();
        if (priced.Count == 0) return null;
        return priced.All(c => c is { Low: not null, High: null }) ? LadderMedian(priced) : BucketMedian(priced);
    }

    /// <summary>
    /// For a figure reported in fixed steps (CPI YoY, unemployment: 0.1), the single printed value the
    /// ladder makes likeliest. "Above 3.6" pays only on 3.7 or more, so P(print = 3.7) = P(&gt;3.6) − P(&gt;3.7).
    /// </summary>
    public static double? LikeliestPrint(IEnumerable<RangeContract> ladder, double step)
    {
        if (Ladder(ladder) is not { Count: > 0 } points) return null;
        var best = (Value: points[0].X, P: 1 - points[0].P); // at or below the lowest strike
        for (var i = 0; i < points.Count; i++)
        {
            var p = points[i].P - (i + 1 < points.Count ? points[i + 1].P : 0);
            if (p > best.P) best = (Round(points[i].X + step, step), p);
        }
        return best.Value;
    }

    /// <summary>
    /// The odds the print comes in above, at, or below the consensus. Null when the ladder has no strike
    /// at the consensus or one step under it.
    /// </summary>
    public static SurpriseOdds? Surprise(IEnumerable<RangeContract> ladder, double consensus, double step)
    {
        if (Ladder(ladder) is not { Count: > 0 } points) return null;
        double? Above(double x)
        {
            var i = points.FindIndex(p => Math.Abs(p.X - x) < step / 100);
            return i < 0 ? null : points[i].P;
        }
        if (Above(consensus) is not { } hotter || Above(consensus - step) is not { } atOrAbove) return null;
        var cooler = 1 - atOrAbove;
        return new SurpriseOdds(hotter, Math.Max(0, atOrAbove - hotter), cooler);
    }

    // "Above X" prices sorted by X. Prices are forced to never rise, as real odds can't.
    private static List<(double X, double P)>? Ladder(IEnumerable<RangeContract> contracts)
    {
        var priced = contracts.Where(c => c is { Low: not null, High: null } && c.Price is not null)
            .Select(c => (X: c.Low!.Value, P: c.Price!.Value)).OrderBy(p => p.X).ToList();
        for (var i = 1; i < priced.Count; i++)
            if (priced[i].P > priced[i - 1].P) priced[i] = priced[i] with { P = priced[i - 1].P };
        return priced;
    }

    private static double Round(double x, double step) => Math.Round(x / step) * step;

    // P(value > X) falls as X rises; the median is where it crosses one half.
    private static double? LadderMedian(List<RangeContract> ladder)
    {
        var points = ladder.Select(c => (X: c.Low!.Value, P: c.Price!.Value)).OrderBy(p => p.X).ToList();
        if (points[0].P < 0.5 || points[^1].P > 0.5) return null; // the ladder doesn't reach the middle
        for (var i = 1; i < points.Count; i++)
        {
            var (a, b) = (points[i - 1], points[i]);
            if (b.P > 0.5) continue;
            return a.P == b.P ? (a.X + b.X) / 2 : a.X + (a.P - 0.5) / (a.P - b.P) * (b.X - a.X);
        }
        return points[0].X;
    }

    // Buckets: scale to sum to 1, then walk up until half the odds are behind us.
    private static double? BucketMedian(List<RangeContract> buckets)
    {
        var sorted = buckets.OrderBy(c => c.Low ?? double.MinValue).ToList();
        var total = sorted.Sum(c => c.Price!.Value);
        if (total <= 0) return null;
        double seen = 0;
        foreach (var c in sorted)
        {
            var p = c.Price!.Value / total;
            if (seen + p >= 0.5)
            {
                if (c.Low is not { } lo) return c.High;
                if (c.High is not { } hi) return lo;
                return lo + (0.5 - seen) / p * (hi - lo);
            }
            seen += p;
        }
        return sorted[^1].Low;
    }
}
