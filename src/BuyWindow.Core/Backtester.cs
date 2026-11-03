namespace BuyWindow.Core;

public enum Verdict { LikelyEdge, NoClearEdge, LikelyWorse }

/// <summary>One finished month. A positive <see cref="EdgePercent"/> means the signal bought cheaper than the first day.</summary>
public sealed record BacktestMonth(
    DateOnly BuyDate,
    BuyReason Reason,
    double BuyPrice,
    double FirstDayOpen,
    double MonthLow,
    double EdgePercent,
    double SignalAboveLowPercent,
    double BaselineAboveLowPercent);

public sealed record BacktestResult(
    IReadOnlyList<BacktestMonth> Months,
    ConfidenceInterval Edge,
    Verdict Verdict,
    int SignalMonths,
    double MeanSignalAboveLowPercent,
    double MeanBaselineAboveLowPercent);

public static class Backtester
{
    public static Verdict VerdictFor(ConfidenceInterval edge)
    {
        if (edge.Low > 0) return Verdict.LikelyEdge;
        return edge.High < 0 ? Verdict.LikelyWorse : Verdict.NoClearEdge;
    }

    public static BacktestResult Run(IReadOnlyList<PriceBar> bars, IReadOnlyList<DayScore> scores)
    {
        var months = new List<BacktestMonth>();
        var scoreDates = scores.Select(s => s.Date).ToHashSet();

        foreach (var monthBars in CompleteMonths.Of(bars))
        {
            if (!scoreDates.Contains(monthBars[0].Date)) continue;

            var days = monthBars.Select(b => b.Date).ToList();
            if (BuyRule.Decide(days, scores) is not { } decision) continue;

            var buyPrice = monthBars.First(b => b.Date == decision.BuyDate).Open;
            var firstOpen = monthBars[0].Open;
            var low = monthBars.Min(b => b.Low);
            months.Add(new BacktestMonth(
                decision.BuyDate,
                decision.Reason,
                buyPrice,
                firstOpen,
                low,
                (firstOpen - buyPrice) / firstOpen * 100,
                (buyPrice - low) / low * 100,
                (firstOpen - low) / low * 100));
        }

        var edge = Statistics.MeanConfidenceInterval(months.Select(m => m.EdgePercent).ToList());
        return new BacktestResult(
            months,
            edge,
            VerdictFor(edge),
            months.Count(m => m.Reason == BuyReason.Signal),
            months.Count == 0 ? 0 : months.Average(m => m.SignalAboveLowPercent),
            months.Count == 0 ? 0 : months.Average(m => m.BaselineAboveLowPercent));
    }
}
