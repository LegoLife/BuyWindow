namespace BuyWindow.Core;

/// <summary>How far a price sits below the highest close in the history we hold.</summary>
public sealed record OffHigh(double High, DateOnly HighDate, double Price, DateOnly HistoryStart)
{
    /// <summary>0 at the high, 0.25 when 25% below it.</summary>
    public double Below => High <= 0 ? 0 : Math.Max(0, 1 - Price / High);

    /// <summary>
    /// The highest adjusted close in <paramref name="bars"/>, against <paramref name="price"/> or the
    /// latest close. History is capped at <see cref="HistoryWindow.MaxYears"/> years, so an older peak
    /// (say, a stock's 2000 high) isn't seen: this is the high of the years we have.
    /// </summary>
    public static OffHigh? From(IReadOnlyList<PriceBar> bars, double? price = null)
    {
        if (bars.Count == 0) return null;
        var peak = bars[0];
        foreach (var b in bars)
            if (b.Close >= peak.Close) peak = b;
        var now = price ?? bars[^1].Close;
        // A live price above every close is a new high.
        return now > peak.Close
            ? new OffHigh(now, bars[^1].Date, now, bars[0].Date)
            : new OffHigh(peak.Close, peak.Date, now, bars[0].Date);
    }
}
