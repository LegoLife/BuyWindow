namespace BuyWindow.Core;

public enum BuyReason { Signal, Deadline }

/// <summary>When to buy in a month. <see cref="SignalDate"/> is null when the deadline forced the purchase.</summary>
public sealed record BuyDecision(BuyReason Reason, DateOnly? SignalDate, DateOnly BuyDate);

public static class BuyRule
{
    /// <summary>Fixed across all tickers. Changing it needs a recorded reason; see the decision log in the README.</summary>
    public const double Threshold = 70;

    /// <summary>Applies the monthly rule; returns null while the month can still go either way.</summary>
    public static BuyDecision? Decide(IReadOnlyList<DateOnly> tradingDays, IEnumerable<DayScore> scores)
    {
        var byDate = scores.ToDictionary(s => s.Date);

        // The previous month's last close can't buy in its own month, so it counts toward this month's first open.
        var closeBefore = MarketCalendar.PreviousTradingDay(tradingDays[0]);
        if (byDate.TryGetValue(closeBefore, out var before) && before.Total >= Threshold)
            return new BuyDecision(BuyReason.Signal, closeBefore, tradingDays[0]);

        for (var i = 0; i < tradingDays.Count - 1; i++)
        {
            if (!byDate.TryGetValue(tradingDays[i], out var score)) return null;
            if (score.Total >= Threshold)
                return new BuyDecision(BuyReason.Signal, tradingDays[i], tradingDays[i + 1]);
        }
        return new BuyDecision(BuyReason.Deadline, null, tradingDays[^1]);
    }
}
