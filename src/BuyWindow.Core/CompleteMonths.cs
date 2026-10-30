namespace BuyWindow.Core;

internal static class CompleteMonths
{
    /// <summary>Bars grouped by month, leaving out the latest month because it may still be running.</summary>
    public static IEnumerable<IReadOnlyList<PriceBar>> Of(IReadOnlyList<PriceBar> bars)
    {
        if (bars.Count == 0) yield break;

        var latest = bars[^1].Date;
        foreach (var month in bars.GroupBy(b => (b.Date.Year, b.Date.Month)))
        {
            if (month.Key != (latest.Year, latest.Month))
                yield return month.ToList();
        }
    }
}
