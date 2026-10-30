namespace BuyWindow.Core.Tests;

public class HistoryWindowTests
{
    private static PriceBar Bar(string date) => new(DateOnly.Parse(date), 10, 10, 10, 10, 100);

    [Fact]
    public void Keeps_at_most_the_last_ten_years()
    {
        var bars = new[] { Bar("2014-12-31"), Bar("2015-01-02"), Bar("2015-01-05") };
        var kept = HistoryWindow.Apply(bars, new DateOnly(2025, 1, 2));
        Assert.Equal([new DateOnly(2015, 1, 2), new DateOnly(2015, 1, 5)], kept.Select(b => b.Date));
    }

    [Fact]
    public void Uses_everything_when_a_ticker_has_less_than_ten_years()
    {
        var bars = new[] { Bar("2021-07-29"), Bar("2021-07-30") };
        Assert.Equal(2, HistoryWindow.Apply(bars, new DateOnly(2026, 10, 30)).Count);
    }

    [Fact]
    public void Drops_everything_before_a_trading_gap_longer_than_thirty_days()
    {
        var bars = new[] { Bar("2022-02-25"), Bar("2024-10-21"), Bar("2024-10-22") };
        var kept = HistoryWindow.Apply(bars, new DateOnly(2026, 10, 30));
        Assert.Equal(new DateOnly(2024, 10, 21), kept[0].Date);
        Assert.Equal(2, kept.Count);
    }

    [Fact]
    public void Keeps_a_gap_of_exactly_thirty_days()
    {
        var bars = new[] { Bar("2024-01-01"), Bar("2024-01-31") };
        Assert.Equal(2, HistoryWindow.Apply(bars, new DateOnly(2026, 10, 30)).Count);
    }

    [Fact]
    public void Uses_the_most_recent_gap_when_there_are_several()
    {
        var bars = new[] { Bar("2020-01-02"), Bar("2021-01-04"), Bar("2022-01-03"), Bar("2022-01-04") };
        var kept = HistoryWindow.Apply(bars, new DateOnly(2026, 10, 30));
        Assert.Equal(new DateOnly(2022, 1, 3), kept[0].Date);
    }

    [Fact]
    public void Sorts_bars_that_arrive_out_of_order()
    {
        var bars = new[] { Bar("2024-01-03"), Bar("2024-01-02") };
        var kept = HistoryWindow.Apply(bars, new DateOnly(2026, 10, 30));
        Assert.Equal(new DateOnly(2024, 1, 2), kept[0].Date);
    }
}
