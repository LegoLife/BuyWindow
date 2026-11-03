namespace BuyWindow.Core.Tests;

public class BuyRuleTests
{
    private static readonly DateOnly[] Days =
    [
        new(2026, 11, 2), new(2026, 11, 3), new(2026, 11, 4), new(2026, 11, 5),
    ];

    private static DayScore Score(DateOnly date, double total) =>
        new(date, total, 0, 0, 0, 0, 0, 0, 0, 0, false);

    [Fact]
    public void First_close_at_the_threshold_buys_at_the_next_open()
    {
        var decision = BuyRule.Decide(Days, [Score(Days[0], 69.9), Score(Days[1], 70), Score(Days[2], 95)]);

        Assert.Equal(BuyReason.Signal, decision!.Reason);
        Assert.Equal(Days[1], decision.SignalDate);
        Assert.Equal(Days[2], decision.BuyDate);
    }

    [Fact]
    public void No_signal_by_the_second_to_last_close_buys_at_the_last_open()
    {
        var decision = BuyRule.Decide(Days, [Score(Days[0], 10), Score(Days[1], 10), Score(Days[2], 10)]);

        Assert.Equal(BuyReason.Deadline, decision!.Reason);
        Assert.Null(decision.SignalDate);
        Assert.Equal(Days[3], decision.BuyDate);
    }

    [Fact]
    public void A_high_score_on_the_last_day_is_too_late_for_this_month()
    {
        var decision = BuyRule.Decide(Days, [Score(Days[0], 10), Score(Days[1], 10), Score(Days[2], 10), Score(Days[3], 99)]);
        Assert.Equal(BuyReason.Deadline, decision!.Reason);
    }

    [Fact]
    public void A_high_score_on_the_previous_months_last_close_buys_at_the_first_open()
    {
        var october30 = new DateOnly(2026, 10, 30);

        var decision = BuyRule.Decide(Days, [Score(october30, 75)]);

        Assert.Equal(BuyReason.Signal, decision!.Reason);
        Assert.Equal(october30, decision.SignalDate);
        Assert.Equal(Days[0], decision.BuyDate);
    }

    [Fact]
    public void A_high_score_earlier_in_the_previous_month_does_not_carry_over()
    {
        Assert.Null(BuyRule.Decide(Days, [Score(new DateOnly(2026, 10, 29), 75), Score(new DateOnly(2026, 10, 30), 10)]));
    }

    [Fact]
    public void Undecided_while_the_month_is_still_open()
    {
        Assert.Null(BuyRule.Decide(Days, [Score(Days[0], 10), Score(Days[1], 10)]));
    }
}
