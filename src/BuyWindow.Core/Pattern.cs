namespace BuyWindow.Core;

/// <summary>How far a trading day of the month typically sits from that month's average close, in percent.</summary>
public sealed record PatternDay(int TradingDay, ConfidenceInterval Range, int ApproxDayOfMonth);

public sealed record PatternWindow(int FirstTradingDay, int LastTradingDay, int ApproxFirstDayOfMonth, int ApproxLastDayOfMonth, ConfidenceInterval Range);

public sealed record PatternResult(int Months, IReadOnlyList<PatternDay> Days, PatternWindow? BestWindow);

public static class Pattern
{
    private const int WindowLength = 5;

    public static PatternResult Compute(IReadOnlyList<PriceBar> bars)
    {
        var months = CompleteMonths.Of(bars)
            .Select(month =>
            {
                var average = month.Average(b => b.Close);
                return month.Select(b => (b.Date, Deviation: (b.Close - average) / average * 100)).ToList();
            })
            .ToList();

        if (months.Count == 0) return new PatternResult(0, [], null);

        var days = new List<PatternDay>();
        for (var day = 1; day <= months.Max(m => m.Count); day++)
        {
            var values = months.Where(m => m.Count >= day).Select(m => m[day - 1]).ToList();
            if (values.Count * 2 < months.Count) break; // too few months reach this far

            days.Add(new PatternDay(
                day,
                Statistics.MeanConfidenceInterval(values.Select(v => v.Deviation).ToList()),
                (int)Math.Round(values.Average(v => v.Date.Day))));
        }

        return new PatternResult(months.Count, days, BestWindow(months, days));
    }

    private static PatternWindow? BestWindow(List<List<(DateOnly Date, double Deviation)>> months, List<PatternDay> days)
    {
        var lastStart = months.Min(m => m.Count) - WindowLength + 1;
        if (lastStart < 1 || months.Count < 2) return null;

        // Searching many windows makes one look good by luck, so each must clear a stricter bar.
        var confidence = 1 - 0.05 / lastStart;

        ConfidenceInterval? lowest = null;
        var lowestStart = 0;
        for (var start = 1; start <= lastStart; start++)
        {
            var range = Statistics.MeanConfidenceInterval(
                months.Select(m => m.Skip(start - 1).Take(WindowLength).Average(d => d.Deviation)).ToList(),
                confidence);
            if (lowest is null || range.Mean < lowest.Mean)
            {
                lowest = range;
                lowestStart = start;
            }
        }

        if (lowest is null || lowest.High >= 0) return null;

        var lastDay = lowestStart + WindowLength - 1;
        return new PatternWindow(lowestStart, lastDay, days[lowestStart - 1].ApproxDayOfMonth, days[lastDay - 1].ApproxDayOfMonth, lowest);
    }
}
