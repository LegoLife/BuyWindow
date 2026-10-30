namespace BuyWindow.Core;

/// <summary>One purchase seen in hindsight. <see cref="ScoreBefore"/> comes from the close before it.</summary>
public sealed record BuyReview(
    DateOnly BuyDate,
    BuyReason Reason,
    double? ScoreBefore,
    double PricePaid,
    double MonthLow,
    bool MonthFinished,
    double? PriceLater,
    double? ChangeLaterPercent);

public static class BuyReviews
{
    public const int LaterTradingDays = 20;

    public static IReadOnlyList<BuyReview> Since(TickerAnalysis analysis, DateOnly from)
    {
        var bars = analysis.Bars;
        if (bars.Count == 0) return [];

        var purchases = analysis.Backtest.Months.Select(m => (m.BuyDate, m.Reason)).ToList();

        // The backtest stops at finished months, which leaves the latest one to decide from the calendar.
        var latest = bars[^1].Date;
        if (BuyRule.Decide(MarketCalendar.TradingDays(latest.Year, latest.Month), analysis.Scores.Where(s => s.Date.Year == latest.Year && s.Date.Month == latest.Month)) is { } decision)
            purchases.Add((decision.BuyDate, decision.Reason));

        var reviews = new List<BuyReview>();
        foreach (var (buyDate, reason) in purchases.Where(p => p.BuyDate >= from))
        {
            var index = IndexOf(bars, buyDate);
            if (index < 0) continue; // its open hasn't happened yet

            var paid = bars[index].Open;
            var later = index + LaterTradingDays < bars.Count ? bars[index + LaterTradingDays].Close : (double?)null;
            var monthDays = MarketCalendar.TradingDays(buyDate.Year, buyDate.Month);

            reviews.Add(new BuyReview(
                buyDate,
                reason,
                analysis.Scores.LastOrDefault(s => s.Date < buyDate)?.Total,
                paid,
                bars.Where(b => b.Date.Year == buyDate.Year && b.Date.Month == buyDate.Month).Min(b => b.Low),
                latest >= monthDays[^1],
                later,
                later is { } price ? (price - paid) / paid * 100 : null));
        }
        return reviews;
    }

    private static int IndexOf(IReadOnlyList<PriceBar> bars, DateOnly date)
    {
        for (var i = bars.Count - 1; i >= 0; i--)
        {
            if (bars[i].Date == date) return i;
        }
        return -1;
    }
}
