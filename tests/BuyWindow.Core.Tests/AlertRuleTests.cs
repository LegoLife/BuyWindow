namespace BuyWindow.Core.Tests;

public class AlertRuleTests
{
    private static readonly DateOnly BuyDate = new(2026, 11, 4);

    private static TickerStatus Buy() =>
        new(StatusKind.BuyNextOpen, BuyReason.Signal, new DateOnly(2026, 11, 3), BuyDate, 18, false);

    private static TickerStatus Other(StatusKind kind, bool deadlineDue = false) =>
        new(kind, null, null, null, 18, deadlineDue);

    [Fact]
    public void Alerts_about_a_buy_once_the_evening_corrections_are_in()
    {
        var alert = AlertRule.For(Buy(), DateTime.Parse("2026-11-03 20:05"));

        Assert.Equal(AlertKind.Buy, alert!.Kind);
        Assert.Equal("2026-11:buy", alert.Key);
    }

    [Fact]
    public void Holds_a_buy_alert_until_the_corrections_refresh_because_a_correction_could_undo_it()
    {
        Assert.Null(AlertRule.For(Buy(), DateTime.Parse("2026-11-03 18:30")));
        Assert.Null(AlertRule.For(Buy(), DateTime.Parse("2026-11-03 20:04")));
    }

    [Fact]
    public void Still_alerts_the_next_morning_if_the_evening_was_missed()
    {
        Assert.NotNull(AlertRule.For(Buy(), DateTime.Parse("2026-11-04 07:00")));
    }

    [Fact]
    public void Warns_when_stale_data_is_holding_back_a_deadline_buy()
    {
        var alert = AlertRule.For(Other(StatusKind.Stale, deadlineDue: true), DateTime.Parse("2026-11-27 21:00"));

        Assert.Equal(AlertKind.DeadlineBlocked, alert!.Kind);
        Assert.Equal("2026-11:deadline", alert.Key);
    }

    [Theory]
    [InlineData(StatusKind.Waiting)]
    [InlineData(StatusKind.Triggered)]
    [InlineData(StatusKind.NotEnoughData)]
    [InlineData(StatusKind.Stale)]
    public void Stays_quiet_when_there_is_nothing_to_do(StatusKind kind) =>
        Assert.Null(AlertRule.For(Other(kind), DateTime.Parse("2026-11-03 21:00")));
}
