namespace BuyWindow.Core.Tests;

public class PatternTests
{
    /// <summary>Monthly bars across <paramref name="months"/> months, priced by month index and trading-day number.</summary>
    private static IReadOnlyList<PriceBar> Months(int months, Func<int, double> price)
    {
        var bars = new List<PriceBar>();
        for (var m = 0; m < months; m++)
        {
            var first = new DateOnly(2015, 1, 1).AddMonths(m);
            var days = MarketCalendar.TradingDays(first.Year, first.Month);
            for (var d = 0; d < days.Count; d++)
                bars.Add(Fixtures.Bar(days[d], price(d + 1)));
        }
        return bars;
    }

    [Fact]
    public void Measures_each_trading_day_against_its_months_average()
    {
        PriceBar[] bars =
        [
            Fixtures.Bar(new DateOnly(2026, 10, 1), 90), Fixtures.Bar(new DateOnly(2026, 10, 2), 110),
            Fixtures.Bar(new DateOnly(2026, 11, 2), 95), Fixtures.Bar(new DateOnly(2026, 11, 3), 105),
            Fixtures.Bar(new DateOnly(2026, 12, 1), 100), // unfinished month is ignored
        ];

        var pattern = Pattern.Compute(bars);

        Assert.Equal(2, pattern.Months);
        Assert.Equal(1, pattern.Days[0].TradingDay);
        Assert.Equal(-7.5, pattern.Days[0].Range.Mean, 6); // -10% and -5%
        Assert.Equal(7.5, pattern.Days[1].Range.Mean, 6);
        Assert.Equal(2, pattern.Days[0].Range.Count);
    }

    [Fact]
    public void Names_the_window_when_a_dip_clearly_stands_out()
    {
        var random = new Random(7);
        var bars = Months(72, day => (day is >= 8 and <= 12 ? 92 : 100) + random.NextDouble());

        var window = Pattern.Compute(bars).BestWindow;

        Assert.NotNull(window);
        Assert.Equal(8, window.FirstTradingDay);
        Assert.Equal(12, window.LastTradingDay);
    }

    [Fact]
    public void Names_no_window_for_noise()
    {
        var random = new Random(42);
        var bars = Months(72, _ => 100 + random.NextDouble() * 10);

        Assert.Null(Pattern.Compute(bars).BestWindow);
    }
}
