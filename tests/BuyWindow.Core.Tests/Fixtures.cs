namespace BuyWindow.Core.Tests;

internal static class Fixtures
{
    public static PriceBar Bar(DateOnly date, double price, double volume = 1000) =>
        new(date, price, price, price, price, volume);

    /// <summary>One bar per calendar weekday, starting on or after <paramref name="start"/>.</summary>
    public static IReadOnlyList<PriceBar> Series(DateOnly start, int count, Func<int, double> price)
    {
        var bars = new List<PriceBar>();
        for (var date = start; bars.Count < count; date = date.AddDays(1))
        {
            if (date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
                bars.Add(Bar(date, price(bars.Count)));
        }
        return bars;
    }

    public static IReadOnlyList<PriceBar> Falling(DateOnly start, int count) =>
        Series(start, count, i => 200 - i);

    public static IReadOnlyList<PriceBar> Rising(DateOnly start, int count) =>
        Series(start, count, i => 100 + i);
}
