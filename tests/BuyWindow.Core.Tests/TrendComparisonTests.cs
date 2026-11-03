namespace BuyWindow.Core.Tests;

public class TrendComparisonTests
{
    private static DayScore Score(DateOnly date, double total) =>
        new(date, total, 0, 0, 0, 0, 0, 0, 0, 0, false);

    private static IReadOnlyList<DayScore> ScoresFor(IEnumerable<PriceBar> bars, Func<DateOnly, double> total) =>
        bars.Select(b => Score(b.Date, total(b.Date))).ToList();

    [Fact]
    public void Six_indicators_share_the_same_hundred_points()
    {
        var bars = Fixtures.Rising(new DateOnly(2024, 1, 1), 201);

        var rescored = TrendComparison.ScoreWithTrend(bars, ScoresFor(bars, _ => 60));

        // 60 of the old 100 is 50 of the new, plus 16.67 for sitting above the 200-day average.
        Assert.Equal(66.667, rescored[^1].Total, 3);
    }

    [Fact]
    public void Below_the_average_earns_no_trend_points()
    {
        var bars = Fixtures.Falling(new DateOnly(2024, 1, 1), 201);

        var rescored = TrendComparison.ScoreWithTrend(bars, ScoresFor(bars, _ => 60));

        Assert.Equal(0, rescored[^1].TrendPoints);
        Assert.Equal(50, rescored[^1].Total, 6);
    }

    [Fact]
    public void Days_before_the_average_exists_are_left_out()
    {
        var bars = Fixtures.Rising(new DateOnly(2024, 1, 1), 205);

        var rescored = TrendComparison.ScoreWithTrend(bars, ScoresFor(bars, _ => 60));

        Assert.Equal(6, rescored.Count);
        Assert.Equal(bars[199].Date, rescored[0].Date);
    }

    [Fact]
    public void Live_scores_are_unchanged_by_the_comparison()
    {
        var bars = Fixtures.Rising(new DateOnly(2024, 1, 1), 300);
        var scores = ScoresFor(bars, _ => 60);

        TrendComparison.Run(bars, scores);

        Assert.All(scores, s => Assert.Equal(60, s.Total));
    }

    [Fact]
    public void Compares_both_versions_over_the_same_months()
    {
        var bars = Fixtures.Rising(new DateOnly(2024, 1, 1), 400);

        var result = TrendComparison.Run(bars, ScoresFor(bars, _ => 10));

        Assert.NotEmpty(result.Current.Months);
        Assert.Equal(result.Current.Months.Select(m => m.BuyDate), result.WithTrend.Months.Select(m => m.BuyDate));
    }

    [Fact]
    public void Identical_purchases_show_no_difference()
    {
        var bars = Fixtures.Rising(new DateOnly(2024, 1, 1), 400);

        var result = TrendComparison.Run(bars, ScoresFor(bars, _ => 10)); // neither version ever triggers

        Assert.Equal(0, result.MonthsChanged);
        Assert.Equal(0, result.Difference.Mean, 6);
        Assert.Equal(Verdict.NoClearEdge, result.Verdict);
    }

    [Fact]
    public void A_positive_difference_means_the_added_indicator_paid_less()
    {
        // Rising prices, so an earlier purchase is cheaper. A score of 65 can only trigger with the
        // added points: 65 * 5/6 + 16.67 = 70.8, so that version buys early and the current one waits.
        var bars = Fixtures.Rising(new DateOnly(2024, 1, 1), 400);

        var result = TrendComparison.Run(bars, ScoresFor(bars, _ => 65));

        Assert.True(result.MonthsChanged > 0);
        Assert.True(result.Difference.Mean > 0);
        Assert.Equal(Verdict.LikelyEdge, result.Verdict);
    }

    [Fact]
    public void Too_little_history_gives_no_months_and_no_verdict()
    {
        var bars = Fixtures.Rising(new DateOnly(2024, 1, 1), 100);

        var result = TrendComparison.Run(bars, ScoresFor(bars, _ => 10));

        Assert.Empty(result.WithTrend.Months);
        Assert.Equal(Verdict.NoClearEdge, result.Verdict);
    }
}
