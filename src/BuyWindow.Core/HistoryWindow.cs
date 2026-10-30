namespace BuyWindow.Core;

/// <summary>Decides which part of a ticker's history counts as real, comparable trading.</summary>
public static class HistoryWindow
{
    public const int MaxYears = 10;
    public const int MaxGapDays = 30;

    public static IReadOnlyList<PriceBar> Apply(IEnumerable<PriceBar> bars, DateOnly asOf)
    {
        var sorted = bars.OrderBy(b => b.Date).ToList();

        // A long halt usually means the business before it isn't comparable (Yandex -> Nebius).
        var start = 0;
        for (var i = 1; i < sorted.Count; i++)
        {
            if (sorted[i].Date.DayNumber - sorted[i - 1].Date.DayNumber > MaxGapDays)
                start = i;
        }

        var earliest = asOf.AddYears(-MaxYears);
        return sorted.Skip(start).Where(b => b.Date >= earliest).ToList();
    }
}
