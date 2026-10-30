namespace BuyWindow.Core.Tests;

public class AnalyzerTests
{
    private static DayScore Score(DateOnly date, double total) =>
        new(date, total, 0, 0, 0, 0, 0, 0, 0, 0, false);

    /// <summary>Low scores for every November 2026 session through <paramref name="through"/>, except <paramref name="high"/>.</summary>
    private static List<DayScore> November(string through, string? high = null) =>
        MarketCalendar.TradingDays(2026, 11)
            .Where(d => d <= DateOnly.Parse(through))
            .Select(d => Score(d, high is not null && d == DateOnly.Parse(high) ? 80 : 10))
            .ToList();

    private static TickerStatus Status(List<DayScore> scores, string lastBar, string now, string? lastMarket = null) =>
        Analyzer.StatusFor(scores, DateOnly.Parse(lastBar), DateOnly.Parse(lastMarket ?? lastBar), DateTime.Parse(now));

    [Fact]
    public void Waits_when_nothing_has_triggered()
    {
        var status = Status(November("2026-11-03"), "2026-11-03", "2026-11-03 21:00");

        Assert.Equal(StatusKind.Waiting, status.Kind);
        Assert.Equal(18, status.TradingDaysLeft);
    }

    [Fact]
    public void Says_buy_the_evening_a_signal_triggers()
    {
        var status = Status(November("2026-11-03", high: "2026-11-03"), "2026-11-03", "2026-11-03 21:00");

        Assert.Equal(StatusKind.BuyNextOpen, status.Kind);
        Assert.Equal(BuyReason.Signal, status.Reason);
        Assert.Equal(new DateOnly(2026, 11, 4), status.BuyDate);
    }

    [Fact]
    public void Shows_triggered_for_the_rest_of_the_month_after_the_buy_date_passes()
    {
        var status = Status(November("2026-11-05", high: "2026-11-03"), "2026-11-05", "2026-11-05 21:00");

        Assert.Equal(StatusKind.Triggered, status.Kind);
        Assert.Equal(new DateOnly(2026, 11, 3), status.SignalDate);
        Assert.Equal(new DateOnly(2026, 11, 4), status.BuyDate);
    }

    [Fact]
    public void Says_buy_at_the_deadline_when_no_signal_came()
    {
        var status = Status(November("2026-11-27"), "2026-11-27", "2026-11-27 21:00");

        Assert.Equal(StatusKind.BuyNextOpen, status.Kind);
        Assert.Equal(BuyReason.Deadline, status.Reason);
        Assert.Equal(new DateOnly(2026, 11, 30), status.BuyDate);
    }

    [Fact]
    public void Rolls_to_the_new_month_after_its_last_close()
    {
        var status = Status(November("2026-11-30"), "2026-11-30", "2026-11-30 21:00");

        Assert.Equal(StatusKind.Waiting, status.Kind);
        Assert.Equal(22, status.TradingDaysLeft); // December 2026
    }

    [Fact]
    public void Holds_back_a_signal_when_the_ticker_is_stale()
    {
        var status = Status(November("2026-11-03", high: "2026-11-03"), "2026-11-03", "2026-11-04 21:00");

        Assert.Equal(StatusKind.Stale, status.Kind);
        Assert.Null(status.BuyDate);
    }

    [Fact]
    public void Holds_back_when_the_market_data_is_stale()
    {
        var status = Status(November("2026-11-04"), "2026-11-04", "2026-11-04 21:00", lastMarket: "2026-11-03");
        Assert.Equal(StatusKind.Stale, status.Kind);
    }

    [Fact]
    public void A_stale_ticker_warns_when_the_deadline_buy_is_due()
    {
        var status = Status(November("2026-11-25"), "2026-11-25", "2026-11-27 21:00");

        Assert.Equal(StatusKind.Stale, status.Kind);
        Assert.True(status.DeadlineDue);
    }

    [Fact]
    public void A_stale_ticker_that_already_triggered_has_no_deadline_due()
    {
        var status = Status(November("2026-11-25", high: "2026-11-05"), "2026-11-25", "2026-11-27 21:00");
        Assert.False(status.DeadlineDue);
    }

    [Fact]
    public void Reports_not_enough_data_when_a_fresh_ticker_has_no_scores()
    {
        var status = Status([], "2026-11-03", "2026-11-03 21:00");
        Assert.Equal(StatusKind.NotEnoughData, status.Kind);
    }

    [Fact]
    public void Under_five_years_is_low_confidence_and_hides_the_pattern()
    {
        var market = Fixtures.Rising(new DateOnly(2020, 1, 1), 1800);
        var bars = Fixtures.Series(new DateOnly(2024, 1, 1), 300, i => 100 + i % 7);

        var analysis = Analyzer.Analyze(bars, market, bars[^1].Date.ToDateTime(new TimeOnly(21, 0)));

        Assert.True(analysis.LowConfidence);
        Assert.Null(analysis.Pattern);
    }

    [Fact]
    public void Five_years_or_more_shows_the_pattern()
    {
        var market = Fixtures.Rising(new DateOnly(2015, 1, 1), 2600);
        var bars = Fixtures.Series(new DateOnly(2018, 1, 1), 1400, i => 100 + i % 7);

        var analysis = Analyzer.Analyze(bars, market, bars[^1].Date.ToDateTime(new TimeOnly(21, 0)));

        Assert.False(analysis.LowConfidence);
        Assert.NotNull(analysis.Pattern);
    }
}
