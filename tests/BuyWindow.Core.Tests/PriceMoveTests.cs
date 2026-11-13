namespace BuyWindow.Core.Tests;

public class PriceMoveTests
{
    [Theory]
    [InlineData(105, 100, 5, 5, true, false)]
    [InlineData(95, 100, -5, -5, false, true)]
    [InlineData(100, 100, 0, 0, false, false)]
    public void Compares_the_price_with_the_previous_close(double price, double previous, double change, double percent, bool up, bool down)
    {
        var move = new PriceMove(price, previous);
        Assert.Equal(change, move.Change, 6);
        Assert.Equal(percent, move.ChangePercent, 6);
        Assert.Equal(up, move.IsUp);
        Assert.Equal(down, move.IsDown);
    }

    [Theory]
    [InlineData("2026-11-03 09:29", false)] // before the open
    [InlineData("2026-11-03 09:30", true)]
    [InlineData("2026-11-03 16:30", true)]  // catching the closing price
    [InlineData("2026-11-03 16:31", false)]
    [InlineData("2026-11-07 12:00", false)] // Saturday
    [InlineData("2026-11-26 12:00", false)] // Thanksgiving
    public void Live_prices_move_only_during_the_session(string eastern, bool moving) =>
        Assert.Equal(moving, MarketCalendar.QuotesMoving(DateTime.Parse(eastern, System.Globalization.CultureInfo.InvariantCulture)));
}
