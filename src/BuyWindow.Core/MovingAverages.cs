namespace BuyWindow.Core;

/// <summary>One moving average of a ticker's closes. Values line up with the bars; null means still warming up.</summary>
public sealed record MovingAverage(int Period, IReadOnlyList<double?> Values)
{
    public double? Latest => Values.Count > 0 ? Values[^1] : null;
}

/// <summary>The averages shown on the ticker page. They're for information only and don't affect the score.</summary>
public static class MovingAverages
{
    public static readonly IReadOnlyList<int> Periods = [60, 90, 200];

    public static IReadOnlyList<MovingAverage> For(IReadOnlyList<PriceBar> bars)
    {
        var closes = bars.Select(b => b.Close).ToArray();
        return Periods.Select(p => new MovingAverage(p, Indicators.Sma(closes, p))).ToList();
    }

    /// <summary>How far the close is above (positive) or below (negative) the average.</summary>
    public static double PercentFrom(double close, double average) => (close - average) / average * 100;
}
