namespace BuyWindow.Core.Tests;

public class FedOddsTests
{
    private static readonly DateOnly Dec9 = new(2026, 12, 9);

    [Fact]
    public void Groups_contracts_into_hike_hold_and_cut()
    {
        var meeting = FedOdds.Meeting(Dec9,
        [
            new("H26", 0.02, 0.02, null),
            new("H25", 0.70, 0.70, null),
            new("H0", 0.20, 0.20, null),
            new("C25", 0.08, 0.08, null),
        ])!;

        Assert.Equal(0.72, meeting.Hike, 6);
        Assert.Equal(0.20, meeting.Hold, 6);
        Assert.Equal(0.08, meeting.Cut, 6);
        Assert.Equal("hike", meeting.Likeliest);
    }

    [Fact]
    public void Scales_prices_that_do_not_sum_to_a_dollar()
    {
        var meeting = FedOdds.Meeting(Dec9, [new("H0", 0.60, 0.60, null), new("C25", 0.60, 0.60, null)])!;
        Assert.Equal(0.5, meeting.Hold, 6);
        Assert.Equal(0.5, meeting.Cut, 6);
    }

    [Fact]
    public void Uses_the_last_trade_when_there_is_no_ask() =>
        Assert.Equal(0.4, FedOdds.Price(new("H0", 0, 0, 0.4)));

    [Fact]
    public void Uses_the_middle_of_bid_and_ask() =>
        Assert.Equal(0.45, FedOdds.Price(new("H0", 0.44, 0.46, 0.9))!.Value, 6);

    [Fact]
    public void No_prices_means_no_meeting() =>
        Assert.Null(FedOdds.Meeting(Dec9, [new("H0", null, null, null)]));
}
