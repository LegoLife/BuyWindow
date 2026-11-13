namespace BuyWindow.Core;

/// <summary>What the Lynch checks said on one purchase day, using only filings made by then.</summary>
public sealed record LynchAtBuy(DateOnly BuyDate, BuyReason Reason, double? ScoreBefore, int Passed, int Checked)
{
    public double? Share => Checked == 0 ? null : (double)Passed / Checked;
}

/// <summary>
/// Whether the buy score and the Peter Lynch checks agree. <see cref="Correlation"/> compares every day's
/// score with that day's share of Lynch rules passed: near 0 means they're unrelated.
/// </summary>
public sealed record LynchVsScoreResult(
    IReadOnlyList<LynchAtBuy> Buys,
    double? SignalShare,
    double? DeadlineShare,
    double? Correlation,
    int Days);

public static class LynchVsScore
{
    public static LynchVsScoreResult Run(TickerAnalysis analysis, Financials financials)
    {
        var bars = analysis.Bars.ToDictionary(b => b.Date);
        var scores = analysis.Scores;

        // Filings only change a few times a year, so each filing date's view is worked out once.
        var cache = new Dictionary<DateOnly, Financials>();
        var fileDates = financials.DilutedEps.Concat(financials.Shares).Concat(financials.Equity)
            .Select(f => f.Filed).Distinct().Order().ToList();
        Financials At(DateOnly date)
        {
            var known = fileDates.LastOrDefault(d => d <= date);
            if (!cache.TryGetValue(known, out var f)) cache[known] = f = financials.AsOf(known);
            return f;
        }

        var buys = new List<LynchAtBuy>();
        foreach (var month in analysis.Backtest.Months)
        {
            var lynch = LynchScore.For(At(month.BuyDate), month.BuyPrice);
            buys.Add(new LynchAtBuy(month.BuyDate, month.Reason,
                scores.LastOrDefault(s => s.Date < month.BuyDate)?.Total, lynch.Passed, lynch.Checked));
        }

        var pairs = new List<(double Score, double Share)>();
        foreach (var score in scores)
        {
            var lynch = LynchScore.For(At(score.Date), bars[score.Date].Close);
            if (lynch.Checked > 0) pairs.Add((score.Total, (double)lynch.Passed / lynch.Checked));
        }

        static double? Mean(IEnumerable<double?> values)
        {
            var list = values.OfType<double>().ToList();
            return list.Count == 0 ? null : list.Average();
        }

        return new LynchVsScoreResult(
            buys,
            Mean(buys.Where(b => b.Reason == BuyReason.Signal).Select(b => b.Share)),
            Mean(buys.Where(b => b.Reason == BuyReason.Deadline).Select(b => b.Share)),
            Pearson(pairs),
            pairs.Count);
    }

    private static double? Pearson(IReadOnlyList<(double X, double Y)> pairs)
    {
        if (pairs.Count < 20) return null;
        var mx = pairs.Average(p => p.X);
        var my = pairs.Average(p => p.Y);
        var sxy = pairs.Sum(p => (p.X - mx) * (p.Y - my));
        var sxx = pairs.Sum(p => (p.X - mx) * (p.X - mx));
        var syy = pairs.Sum(p => (p.Y - my) * (p.Y - my));
        return sxx == 0 || syy == 0 ? null : sxy / Math.Sqrt(sxx * syy);
    }
}
