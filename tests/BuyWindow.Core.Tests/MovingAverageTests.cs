namespace BuyWindow.Core.Tests;

public class MovingAverageTests
{
    [Fact]
    public void Each_average_starts_once_it_has_enough_closes()
    {
        var bars = Fixtures.Rising(new DateOnly(2024, 1, 1), 100);

        var averages = MovingAverages.For(bars);

        Assert.Equal(["5-day", "20-day", "50-day", "100-day", "200-day", "YTD"], averages.Select(a => a.Name));
        Assert.Null(averages[0].Values[3]);
        Assert.Equal(102, averages[0].Values[4]!.Value, 6);    // closes 100 to 104
        Assert.Equal(174.5, averages[2].Latest!.Value, 6);     // closes 150 to 199
        Assert.Null(averages[3].Values[98]);
        Assert.Equal(149.5, averages[3].Latest!.Value, 6);     // closes 100 to 199
        Assert.Null(averages[4].Latest);
    }

    [Fact]
    public void Year_to_date_average_restarts_each_january()
    {
        PriceBar[] bars =
        [
            Fixtures.Bar(new DateOnly(2025, 12, 30), 100),
            Fixtures.Bar(new DateOnly(2025, 12, 31), 200),
            Fixtures.Bar(new DateOnly(2026, 1, 2), 10),
            Fixtures.Bar(new DateOnly(2026, 1, 5), 20),
            Fixtures.Bar(new DateOnly(2026, 1, 6), 60),
        ];

        var ytd = MovingAverages.YearToDate(bars);

        Assert.Equal([100, 150, 10, 15, 30], ytd.Select(v => v!.Value));
    }

    [Theory]
    [InlineData(110, 100, 10)]
    [InlineData(90, 100, -10)]
    public void Reports_how_far_the_close_is_from_its_average(double close, double average, double percent) =>
        Assert.Equal(percent, MovingAverages.PercentFrom(close, average), 6);
}
