namespace BuyWindow.Core.Tests;

public class BuyReviewTests
{
    private static readonly DateOnly Nov2 = new(2026, 11, 2);

    private static TickerAnalysis Analysis(int barCount, params (int Bar, double Total)[] highScores)
    {
        var bars = Fixtures.Series(Nov2, barCount, i => 100 + i);
        var scores = bars
            .Select((b, i) => new DayScore(b.Date, highScores.FirstOrDefault(h => h.Bar == i).Total, 0, 0, 0, 0, 0, 0, 0, 0, false))
            .ToList();
        var status = new TickerStatus(StatusKind.Waiting, null, null, null, 0, false);
        return new TickerAnalysis(status, scores, Backtester.Run(bars, scores), null, false, bars[0].Date, bars[^1].Date, bars);
    }

    [Fact]
    public void Shows_a_finished_months_purchase_and_the_price_twenty_trading_days_later()
    {
        // Bar 2 (Nov 4) scores 75, so the rule buys at bar 3's open: 103. Bar 23 closes at 123.
        var analysis = Analysis(30, (2, 75));

        var review = BuyReviews.Since(analysis, Nov2)[0];

        Assert.Equal(new DateOnly(2026, 11, 5), review.BuyDate);
        Assert.Equal(BuyReason.Signal, review.Reason);
        Assert.Equal(75, review.ScoreBefore);
        Assert.Equal(103, review.PricePaid);
        Assert.Equal(100, review.MonthLow);
        Assert.True(review.MonthFinished);
        Assert.Equal(123, review.PriceLater);
        Assert.Equal(19.417, review.ChangeLaterPercent!.Value, 3);
    }

    [Fact]
    public void Includes_this_months_purchase_with_the_low_so_far_and_no_later_price_yet()
    {
        // November has 21 weekdays, so bar 23 is Dec 3. The rule buys at bar 24's open: 124.
        var analysis = Analysis(30, (2, 75), (23, 80));

        var review = BuyReviews.Since(analysis, Nov2)[^1];

        Assert.Equal(new DateOnly(2026, 12, 4), review.BuyDate);
        Assert.Equal(124, review.PricePaid);
        Assert.Equal(121, review.MonthLow);
        Assert.False(review.MonthFinished);
        Assert.Null(review.PriceLater);
        Assert.Null(review.ChangeLaterPercent);
    }

    [Fact]
    public void Leaves_out_a_purchase_whose_open_has_not_happened_yet()
    {
        // Bar 29 is the latest close, so its signal buys at an open that isn't known yet.
        var analysis = Analysis(30, (2, 75), (29, 80));

        Assert.Equal(new DateOnly(2026, 11, 5), Assert.Single(BuyReviews.Since(analysis, Nov2)).BuyDate);
    }

    [Fact]
    public void A_deadline_purchase_shows_the_score_from_the_close_before_it()
    {
        var analysis = Analysis(30);

        var review = BuyReviews.Since(analysis, Nov2)[0];

        Assert.Equal(new DateOnly(2026, 11, 30), review.BuyDate);
        Assert.Equal(BuyReason.Deadline, review.Reason);
        Assert.Equal(0, review.ScoreBefore);
    }

    [Fact]
    public void Starts_from_the_requested_date()
    {
        var analysis = Analysis(30, (2, 75), (23, 80));

        var review = Assert.Single(BuyReviews.Since(analysis, new DateOnly(2026, 12, 1)));

        Assert.Equal(new DateOnly(2026, 12, 4), review.BuyDate);
    }
}
