namespace BuyWindow.Core.Tests;

public class MovingAverageTests
{
    [Fact]
    public void Each_average_starts_once_it_has_enough_closes()
    {
        var bars = Fixtures.Rising(new DateOnly(2024, 1, 1), 100);

        var averages = MovingAverages.For(bars);

        Assert.Equal([60, 90, 200], averages.Select(a => a.Period));
        Assert.Null(averages[0].Values[58]);
        Assert.Equal(129.5, averages[0].Values[59]!.Value, 6); // closes 100 to 159
        Assert.Equal(154.5, averages[1].Latest!.Value, 6);     // closes 110 to 199
        Assert.Null(averages[2].Latest);
    }

    [Theory]
    [InlineData(110, 100, 10)]
    [InlineData(90, 100, -10)]
    public void Reports_how_far_the_close_is_from_its_average(double close, double average, double percent) =>
        Assert.Equal(percent, MovingAverages.PercentFrom(close, average), 6);
}
