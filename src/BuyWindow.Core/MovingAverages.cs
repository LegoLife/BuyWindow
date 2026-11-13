namespace BuyWindow.Core;

/// <summary>
/// One moving average of a ticker's closes. Values line up with the bars; null means still warming up.
/// <see cref="Period"/> is null for the year-to-date average, which restarts each January.
/// </summary>
public sealed record MovingAverage(string Name, int? Period, IReadOnlyList<double?> Values)
{
    public double? Latest => Values.Count > 0 ? Values[^1] : null;
}

/// <summary>The averages shown on the ticker page. They're for information only and don't affect the score.</summary>
public static class MovingAverages
{
    public static readonly IReadOnlyList<int> Periods = [5, 20, 50, 100, 200];

    public static IReadOnlyList<MovingAverage> For(IReadOnlyList<PriceBar> bars)
    {
        var closes = bars.Select(b => b.Close).ToArray();
        return Periods
            .Select(p => new MovingAverage($"{p}-day", p, Indicators.Sma(closes, p)))
            .Append(new MovingAverage("YTD", null, YearToDate(bars)))
            .ToList();
    }

    /// <summary>Each day's average close since the year's first trading day, that day included.</summary>
    public static double?[] YearToDate(IReadOnlyList<PriceBar> bars)
    {
        var result = new double?[bars.Count];
        double sum = 0;
        var count = 0;
        for (var i = 0; i < bars.Count; i++)
        {
            if (i == 0 || bars[i].Date.Year != bars[i - 1].Date.Year)
                (sum, count) = (0, 0);
            sum += bars[i].Close;
            count++;
            result[i] = sum / count;
        }
        return result;
    }

    /// <summary>How far the close is above (positive) or below (negative) the average.</summary>
    public static double PercentFrom(double close, double average) => (close - average) / average * 100;
}
