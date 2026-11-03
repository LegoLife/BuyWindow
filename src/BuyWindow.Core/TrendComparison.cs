namespace BuyWindow.Core;

/// <summary>
/// The current score against the same score with the ticker's 200-day average added.
/// <see cref="Difference"/> is per month: a positive value means the added indicator paid less.
/// </summary>
public sealed record TrendComparisonResult(
    BacktestResult Current,
    BacktestResult WithTrend,
    ConfidenceInterval Difference,
    Verdict Verdict,
    int MonthsChanged);

/// <summary>
/// A test only: nothing live uses these scores. The direction and the threshold are fixed in advance,
/// so the test can't be tuned toward a good result. See the decision log in the README.
/// </summary>
public static class TrendComparison
{
    public const int AveragePeriod = 200;

    // Six indicators sharing 100 points instead of five.
    private const double Rescale = 5.0 / 6.0;

    /// <summary>Rescored days, starting once the ticker has its 200-day average. At or above it earns full points.</summary>
    public static IReadOnlyList<DayScore> ScoreWithTrend(IReadOnlyList<PriceBar> bars, IReadOnlyList<DayScore> scores)
    {
        var average = Indicators.Sma(bars.Select(b => b.Close).ToArray(), AveragePeriod);
        var byDate = bars.Select((bar, i) => (bar, i)).ToDictionary(x => x.bar.Date);

        var result = new List<DayScore>();
        foreach (var score in scores)
        {
            if (!byDate.TryGetValue(score.Date, out var day) || average[day.i] is not { } value) continue;

            result.Add(score with
            {
                RsiPoints = score.RsiPoints * Rescale,
                BollingerPoints = score.BollingerPoints * Rescale,
                MacdPoints = score.MacdPoints * Rescale,
                VolumePoints = score.VolumePoints * Rescale,
                MarketPoints = score.MarketPoints * Rescale,
                TrendPoints = day.bar.Close >= value ? Scoring.MaxPoints * Rescale : 0,
            });
        }
        return result;
    }

    /// <summary>Backtests both versions over the same days, so neither gets months the other can't have.</summary>
    public static TrendComparisonResult Run(IReadOnlyList<PriceBar> bars, IReadOnlyList<DayScore> scores)
    {
        var rescored = ScoreWithTrend(bars, scores);
        var sameDays = rescored.Select(s => s.Date).ToHashSet();

        var current = Backtester.Run(bars, scores.Where(s => sameDays.Contains(s.Date)).ToList());
        var withTrend = Backtester.Run(bars, rescored);

        var currentByMonth = current.Months.ToDictionary(m => (m.BuyDate.Year, m.BuyDate.Month));
        var pairs = withTrend.Months
            .Where(m => currentByMonth.ContainsKey((m.BuyDate.Year, m.BuyDate.Month)))
            .Select(m => (Trend: m, Current: currentByMonth[(m.BuyDate.Year, m.BuyDate.Month)]))
            .ToList();

        var difference = Statistics.MeanConfidenceInterval(pairs.Select(p => p.Trend.EdgePercent - p.Current.EdgePercent).ToList());
        return new TrendComparisonResult(
            current,
            withTrend,
            difference,
            Backtester.VerdictFor(difference),
            pairs.Count(p => p.Trend.BuyDate != p.Current.BuyDate));
    }
}
