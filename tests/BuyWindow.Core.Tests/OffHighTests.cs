namespace BuyWindow.Core.Tests;

public class OffHighTests
{
    private static PriceBar Bar(int day, double close) => new(new DateOnly(2026, 1, day), close, close, close, close, 1);

    [Fact]
    public void Measures_the_latest_close_against_the_highest()
    {
        var off = OffHigh.From([Bar(1, 50), Bar(2, 100), Bar(3, 75)])!;
        Assert.Equal(100, off.High);
        Assert.Equal(new DateOnly(2026, 1, 2), off.HighDate);
        Assert.Equal(0.25, off.Below, 6);
    }

    [Fact]
    public void Uses_a_live_price_when_given() =>
        Assert.Equal(0.4, OffHigh.From([Bar(1, 100), Bar(2, 90)], price: 60)!.Below, 6);

    [Fact]
    public void A_live_price_above_every_close_is_at_the_high() =>
        Assert.Equal(0, OffHigh.From([Bar(1, 100)], price: 110)!.Below);

    [Fact]
    public void Nothing_without_history() => Assert.Null(OffHigh.From([]));
}
