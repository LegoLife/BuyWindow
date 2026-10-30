namespace BuyWindow.Core.Tests;

public class BacktestTests
{
    private static DayScore Score(DateOnly date, double total) =>
        new(date, total, 0, 0, 0, 0, 0, 0, 0, 0, false);

    [Fact]
    public void Confidence_range_matches_a_worked_t_interval()
    {
        // Differences 1..5: mean 3, standard error 0.7071, t(4, 95%) = 2.776, so 3 +/- 1.963.
        var range = Statistics.MeanConfidenceInterval([1, 2, 3, 4, 5]);
        Assert.Equal(3, range.Mean, 6);
        Assert.Equal(1.037, range.Low, 2);
        Assert.Equal(4.963, range.High, 2);
    }

    [Theory]
    [InlineData(new[] { 1.0, 2, 3, 4, 5 }, Verdict.LikelyEdge)]
    [InlineData(new[] { -1.0, -2, -3, -4, -5 }, Verdict.LikelyWorse)]
    [InlineData(new[] { -3.0, 2, -1, 3, -2 }, Verdict.NoClearEdge)]
    [InlineData(new[] { 5.0 }, Verdict.NoClearEdge)]
    public void Verdict_follows_the_confidence_range(double[] differences, Verdict expected) =>
        Assert.Equal(expected, Backtester.VerdictFor(Statistics.MeanConfidenceInterval(differences)));

    [Fact]
    public void Compares_each_months_purchase_with_the_first_day_and_the_months_low()
    {
        DateOnly nov2 = new(2026, 11, 2), nov3 = new(2026, 11, 3), nov4 = new(2026, 11, 4), dec1 = new(2026, 12, 1);
        PriceBar[] bars =
        [
            new(nov2, Open: 100, High: 101, Low: 99, Close: 100, Volume: 1),
            new(nov3, Open: 98, High: 99, Low: 95, Close: 96, Volume: 1),
            new(nov4, Open: 90, High: 91, Low: 80, Close: 85, Volume: 1),
            new(dec1, Open: 88, High: 88, Low: 88, Close: 88, Volume: 1),
        ];

        var result = Backtester.Run(bars, [Score(nov2, 10), Score(nov3, 75), Score(nov4, 10), Score(dec1, 10)]);

        var month = Assert.Single(result.Months);
        Assert.Equal(nov4, month.BuyDate);
        Assert.Equal(BuyReason.Signal, month.Reason);
        Assert.Equal(90, month.BuyPrice);
        Assert.Equal(100, month.FirstDayOpen);
        Assert.Equal(80, month.MonthLow);
        Assert.Equal(10, month.EdgePercent, 6);             // 10% cheaper than the first day
        Assert.Equal(12.5, month.SignalAboveLowPercent, 6); // 90 vs 80
        Assert.Equal(25, month.BaselineAboveLowPercent, 6); // 100 vs 80
    }

    [Fact]
    public void Skips_the_unfinished_latest_month_and_months_without_a_first_day_score()
    {
        DateOnly oct1 = new(2026, 10, 1), oct2 = new(2026, 10, 2), nov2 = new(2026, 11, 2), nov3 = new(2026, 11, 3), dec1 = new(2026, 12, 1);
        var bars = new[] { oct1, oct2, nov2, nov3, dec1 }.Select(d => Fixtures.Bar(d, 100)).ToArray();

        // October's first day has no score (warm-up); November is whole; December has only begun.
        var result = Backtester.Run(bars, [Score(oct2, 10), Score(nov2, 10), Score(nov3, 10), Score(dec1, 10)]);

        Assert.Equal(new DateOnly(2026, 11, 3), Assert.Single(result.Months).BuyDate);
    }

    [Fact]
    public void Counts_how_often_the_signal_triggered_rather_than_the_deadline()
    {
        DateOnly nov2 = new(2026, 11, 2), nov3 = new(2026, 11, 3), nov4 = new(2026, 11, 4);
        DateOnly dec1 = new(2026, 12, 1), dec2 = new(2026, 12, 2), dec3 = new(2026, 12, 3), jan4 = new(2027, 1, 4);
        var bars = new[] { nov2, nov3, nov4, dec1, dec2, dec3, jan4 }.Select(d => Fixtures.Bar(d, 100)).ToArray();

        var result = Backtester.Run(bars,
        [
            Score(nov2, 80), Score(nov3, 10), Score(nov4, 10),
            Score(dec1, 10), Score(dec2, 10), Score(dec3, 10), Score(jan4, 10),
        ]);

        Assert.Equal(2, result.Months.Count);
        Assert.Equal(1, result.SignalMonths);
    }
}
