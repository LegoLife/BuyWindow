namespace BuyWindow.Core;

public enum StatusKind { Waiting, BuyNextOpen, Triggered, Stale, NotEnoughData }

public sealed record TickerStatus(
    StatusKind Kind,
    BuyReason? Reason,
    DateOnly? SignalDate,
    DateOnly? BuyDate,
    int TradingDaysLeft,
    bool DeadlineDue);

public sealed record TickerAnalysis(
    TickerStatus Status,
    IReadOnlyList<DayScore> Scores,
    BacktestResult Backtest,
    PatternResult? Pattern,
    bool LowConfidence,
    DateOnly? HistoryStart,
    DateOnly? HistoryEnd);

public static class Analyzer
{
    private const int LowConfidenceYears = 5;

    public static TickerAnalysis Analyze(IEnumerable<PriceBar> rawBars, IReadOnlyList<PriceBar> market, DateTime easternNow)
    {
        var bars = HistoryWindow.Apply(rawBars, DateOnly.FromDateTime(easternNow));
        var sortedMarket = market.OrderBy(b => b.Date).ToList();
        var scores = Scoring.ScoreAll(bars, sortedMarket);

        DateOnly? first = bars.Count > 0 ? bars[0].Date : null;
        DateOnly? last = bars.Count > 0 ? bars[^1].Date : null;
        var lowConfidence = first is null || first.Value.AddYears(LowConfidenceYears) > last;

        return new TickerAnalysis(
            StatusFor(scores, last, sortedMarket.Count > 0 ? sortedMarket[^1].Date : null, easternNow),
            scores,
            Backtester.Run(bars, scores),
            lowConfidence ? null : Pattern.Compute(bars),
            lowConfidence,
            first,
            last);
    }

    public static TickerStatus StatusFor(IReadOnlyList<DayScore> scores, DateOnly? lastBar, DateOnly? lastMarket, DateTime easternNow)
    {
        var expected = MarketCalendar.LatestExpectedSession(easternNow);
        var fresh = lastBar >= expected && lastMarket >= expected;

        // Everything is judged from the latest close: the one we hold, or the one we should hold by now.
        var anchor = fresh ? lastBar!.Value : expected;
        var next = MarketCalendar.NextTradingDay(anchor);
        var month = MarketCalendar.TradingDays(next.Year, next.Month);
        var left = month.Count(d => d > anchor);

        // A ticker that finished warming up mid-month is judged only on the days it could be scored.
        var scorableDays = scores.Count > 0 ? month.Where(d => d >= scores[0].Date).ToList() : month;
        var known = scores.Where(s => s.Date >= month[0] && s.Date <= anchor);
        var decision = BuyRule.Decide(scorableDays, known);

        if (!fresh)
        {
            var due = anchor >= month[^2] && decision?.Reason != BuyReason.Signal;
            return new TickerStatus(StatusKind.Stale, null, null, null, left, due);
        }

        if (scores.Count == 0 || scores[^1].Date < anchor)
            return new TickerStatus(StatusKind.NotEnoughData, null, null, null, left, false);

        if (decision is null)
            return new TickerStatus(StatusKind.Waiting, null, null, null, left, false);

        var actNow = decision.BuyDate == next;
        return new TickerStatus(
            actNow ? StatusKind.BuyNextOpen : StatusKind.Triggered,
            decision.Reason,
            decision.SignalDate,
            decision.BuyDate,
            left,
            false);
    }
}
