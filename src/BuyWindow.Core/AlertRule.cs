namespace BuyWindow.Core;

public enum AlertKind { Buy, DeadlineBlocked }

/// <summary>Something worth emailing about. <see cref="Key"/> identifies it within a ticker, so it's only sent once.</summary>
public sealed record TickerAlert(AlertKind Kind, string Key);

public static class AlertRule
{
    public static TickerAlert? For(TickerStatus status, DateTime easternNow)
    {
        if (status.Kind == StatusKind.BuyNextOpen && status.BuyDate is { } buyDate)
        {
            // A correction can change the evening's score, so wait for the final numbers.
            return MarketCalendar.CorrectionsRefreshDone(easternNow)
                ? new TickerAlert(AlertKind.Buy, $"{buyDate:yyyy-MM}:buy")
                : null;
        }

        if (status is { Kind: StatusKind.Stale, DeadlineDue: true })
        {
            var month = MarketCalendar.NextTradingDay(MarketCalendar.LatestExpectedSession(easternNow));
            return new TickerAlert(AlertKind.DeadlineBlocked, $"{month:yyyy-MM}:deadline");
        }

        return null;
    }
}
