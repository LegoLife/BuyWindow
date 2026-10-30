namespace BuyWindow.Core.Tests;

public class ScoringTests
{
    [Theory]
    [InlineData(25, 20)]
    [InlineData(30, 20)]
    [InlineData(45, 10)]
    [InlineData(60, 0)]
    [InlineData(80, 0)]
    public void Rsi_earns_more_the_more_oversold_it_is(double rsi, double points) =>
        Assert.Equal(points, Scoring.RsiPoints(rsi), 6);

    [Theory]
    [InlineData(-0.2, 20)]
    [InlineData(0, 20)]
    [InlineData(0.25, 10)]
    [InlineData(0.5, 0)]
    [InlineData(1.1, 0)]
    public void Bollinger_earns_more_the_nearer_the_lower_band(double percentB, double points) =>
        Assert.Equal(points, Scoring.BollingerPoints(percentB), 6);

    [Theory]
    [InlineData(-1.0, -2.0, -3.0, 20)] // negative, rising two days
    [InlineData(-1.0, -2.0, -1.5, 10)] // negative, rising one day
    [InlineData(-2.0, -1.0, -0.5, 0)]  // negative, still falling
    [InlineData(0.5, -0.5, -1.0, 20)]  // just crossed above zero
    [InlineData(1.0, 0.5, 0.2, 0)]     // already positive: nothing left to fade
    public void Macd_rewards_fading_downward_momentum(double today, double yesterday, double dayBefore, double points) =>
        Assert.Equal(points, Scoring.MacdPoints(today, yesterday, dayBefore), 6);

    [Theory]
    [InlineData(2.5, true, 20)]
    [InlineData(2.0, true, 20)]
    [InlineData(1.5, true, 10)]
    [InlineData(1.0, true, 0)]
    [InlineData(3.0, false, 0)] // heavy buying isn't a sell-off
    public void Volume_rewards_heavy_trading_on_down_days(double ratio, bool downDay, double points) =>
        Assert.Equal(points, Scoring.VolumePoints(ratio, downDay), 6);

    [Theory]
    [InlineData(101, 100, 20)]
    [InlineData(100, 100, 20)]
    [InlineData(99, 100, 0)]
    public void Market_trend_is_all_or_nothing(double close, double average, double points) =>
        Assert.Equal(points, Scoring.MarketPoints(close, average), 6);

    [Fact]
    public void Rsi_matches_the_published_wilder_worked_example()
    {
        // StockCharts' RSI worked example: the first 14-day RSI is 70.53.
        double[] closes = [44.3389, 44.0902, 44.1497, 43.6124, 44.3278, 44.8264, 45.0955, 45.4245,
            45.8433, 46.0826, 45.8931, 46.0328, 45.6140, 46.2820, 46.2820];
        Assert.Equal(70.53, Indicators.Rsi(closes)[14]!.Value, 1);
    }

    [Fact]
    public void Bollinger_percent_b_places_the_close_within_the_band()
    {
        // Twenty closes alternating 9/11: mean 10, stdev 1, band 8..12, so a close of 11 sits at 0.75.
        var closes = Enumerable.Range(0, 20).Select(i => i % 2 == 0 ? 9.0 : 11.0).ToArray();
        Assert.Equal(0.75, Indicators.PercentB(closes)[19]!.Value, 6);
    }

    [Fact]
    public void Macd_histogram_is_zero_for_flat_prices_and_unavailable_during_warmup()
    {
        var histogram = Indicators.MacdHistogram(Enumerable.Repeat(50.0, 40).ToArray());
        Assert.Null(histogram[32]);
        Assert.Equal(0, histogram[33]!.Value, 6);
    }

    [Fact]
    public void Volume_ratio_compares_today_with_the_previous_twenty_days()
    {
        var volumes = Enumerable.Repeat(100.0, 20).Append(250.0).ToArray();
        var ratios = Indicators.VolumeRatio(volumes);
        Assert.Null(ratios[19]);
        Assert.Equal(2.5, ratios[20]!.Value, 6);
    }

    [Fact]
    public void Daily_scores_start_after_warmup_and_total_their_components()
    {
        var bars = Fixtures.Falling(new DateOnly(2024, 1, 1), 60);
        var market = Fixtures.Rising(new DateOnly(2023, 1, 1), 500);

        var scores = Scoring.ScoreAll(bars, market);

        Assert.Equal(bars[Scoring.WarmupBars - 1].Date, scores[0].Date);
        var last = scores[^1];
        Assert.Equal(20, last.MarketPoints);
        Assert.Equal(20, last.RsiPoints); // a steady fall is deeply oversold
        Assert.Equal(last.RsiPoints + last.BollingerPoints + last.MacdPoints + last.VolumePoints + last.MarketPoints, last.Total, 6);
    }

    [Fact]
    public void No_score_without_two_hundred_days_of_market_history()
    {
        var bars = Fixtures.Falling(new DateOnly(2024, 1, 1), 60);
        var market = Fixtures.Rising(new DateOnly(2023, 12, 1), 100);
        Assert.Empty(Scoring.ScoreAll(bars, market));
    }
}
