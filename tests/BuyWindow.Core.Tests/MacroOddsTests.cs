namespace BuyWindow.Core.Tests;

public class MacroOddsTests
{
    private static RangeContract Above(double x, double p) => new(x, null, p, p, null);
    private static RangeContract Bucket(double? lo, double? hi, double p) => new(lo, hi, p, p, null);

    [Fact]
    public void Ladder_median_is_where_the_odds_cross_one_half() =>
        // 4.0 → 84%, 4.1 → 48%: the half-way point is just under 4.1.
        Assert.Equal(4.0 + 0.34 / 0.36 * 0.1,
            MacroOdds.Median([Above(4.2, 0.17), Above(4.0, 0.84), Above(4.1, 0.48)])!.Value, 6);

    [Fact]
    public void Ladder_that_never_reaches_one_half_has_no_median() =>
        Assert.Null(MacroOdds.Median([Above(3.0, 0.40), Above(3.1, 0.20)]));

    [Fact]
    public void Bucket_median_walks_up_to_half_the_odds() =>
        // 25% below 7000, then 50% in 7000-7200: half-way is the middle of that bucket.
        Assert.Equal(7100, MacroOdds.Median(
        [
            Bucket(7200, null, 0.25), Bucket(null, 7000, 0.25), Bucket(7000, 7200, 0.50),
        ])!.Value, 6);

    [Fact]
    public void Buckets_are_scaled_to_sum_to_one() =>
        Assert.Equal(150, MacroOdds.Median([Bucket(100, 200, 0.6), Bucket(200, 300, 0.0)])!.Value, 6);

    [Fact]
    public void No_prices_means_no_median() =>
        Assert.Null(MacroOdds.Median([new RangeContract(1, null, null, null, null)]));

    // November 2026 CPI YoY as priced on 2026-11-25.
    private static readonly RangeContract[] Cpi =
        [Above(3.4, 0.985), Above(3.5, 0.915), Above(3.6, 0.665), Above(3.7, 0.225), Above(3.8, 0.045)];

    [Fact]
    public void Likeliest_print_is_one_step_above_the_strike_it_clears() =>
        // P(3.7) = 66.5% - 22.5% = 44%, more than any other print.
        Assert.Equal(3.7, MacroOdds.LikeliestPrint(Cpi, 0.1)!.Value, 6);

    [Fact]
    public void Likeliest_print_can_be_at_or_below_the_lowest_strike() =>
        Assert.Equal(3.0, MacroOdds.LikeliestPrint([Above(3.0, 0.2), Above(3.1, 0.1)], 0.1)!.Value, 6);

    [Fact]
    public void Surprise_splits_hotter_in_line_and_cooler()
    {
        var s = MacroOdds.Surprise(Cpi, 3.6, 0.1)!;
        Assert.Equal(0.665, s.Hotter, 6);
        Assert.Equal(0.25, s.InLine, 6);
        Assert.Equal(0.085, s.Cooler, 6);
        Assert.Equal("hot", s.Lean);
    }

    [Fact]
    public void Surprise_is_balanced_when_the_sides_are_close() =>
        Assert.Equal("balanced", MacroOdds.Surprise(Cpi, 3.7, 0.1)!.Lean); // 22.5% hotter vs 33.5% cooler

    [Fact]
    public void Surprise_needs_strikes_at_the_consensus() =>
        Assert.Null(MacroOdds.Surprise(Cpi, 5.0, 0.1));

    [Fact]
    public void Odds_that_rise_with_the_strike_are_flattened() =>
        Assert.Equal(0.3, MacroOdds.Surprise([Above(3.0, 0.5), Above(3.1, 0.3), Above(3.2, 0.4)], 3.2, 0.1)!.Hotter, 6);
}
