using BuyWindow.Core;

namespace BuyWindow.Web.Components.Shared;

/// <summary>Plain-English reasons behind "Own it?" and "When?", for the top of a ticker page.</summary>
public static class Why
{
    /// <summary>One sentence per answered rule, saying what the reading means.</summary>
    public static IReadOnlyList<string> Own(OwnResult own) => own.Rules
        .Where(r => r.Passes is not null)
        .OrderBy(r => r.Passes) // failures first
        .Select(r => (r.Name, r.Passes) switch
        {
            ("Forward P/E", true) => $"Forward P/E {r.Reading}: the price is reasonable for the earnings analysts expect next year (under {OwnCheck.MaxForwardPe:0}).",
            ("Forward P/E", false) => $"Forward P/E {r.Reading}: you're paying a lot for next year's expected earnings (wants above 0 and under {OwnCheck.MaxForwardPe:0}).",
            ("PEG", true) => $"PEG {r.Reading}: the price is fair for how fast earnings are growing (under {OwnCheck.MaxPeg:0}).",
            ("PEG", false) => $"PEG {r.Reading}: the price runs ahead of its earnings growth (wants above 0 and under {OwnCheck.MaxPeg:0}).",
            ("Debt/Equity", true) => $"Debt/Equity {r.Reading}: little debt, so a bad year is less likely to sink it (under {OwnCheck.MaxDebtToEquity:0.00}).",
            ("Debt/Equity", false) => $"Debt/Equity {r.Reading}: heavy debt for the size of the company (wants under {OwnCheck.MaxDebtToEquity:0.00}).",
            ("EPS growth", true) => $"EPS growth {r.Reading}: earnings per share are rising.",
            ("EPS growth", false) => $"EPS growth {r.Reading}: earnings per share are shrinking.",
            ("Short float", true) => $"Short float {r.Reading}: few traders are betting it will fall (under {OwnCheck.MaxShortFloat:0%}).",
            ("Short float", false) => $"Short float {r.Reading}: many traders are betting it will fall (wants under {OwnCheck.MaxShortFloat:0%}).",
            _ => $"{r.Name} {r.Reading}.",
        })
        .ToList();

    /// <summary>The buy date (or the latest it can be) and why, from this month's status and score.</summary>
    public static (string Headline, IReadOnlyList<string> Reasons) When(TickerAnalysis analysis, DateOnly today)
    {
        var status = analysis.Status;
        var days = MarketCalendar.TradingDays(today.Year, today.Month);
        var deadline = days.Count > 0 ? days[^1] : today;
        var nextMonth = MarketCalendar.TradingDays(today.AddMonths(1).Year, today.AddMonths(1).Month).FirstOrDefault();
        var latest = analysis.Scores.Count > 0 ? analysis.Scores[^1] : null;
        var atSignal = status.SignalDate is { } s ? analysis.Scores.LastOrDefault(x => x.Date == s) : null;

        var headline = status.Kind switch
        {
            StatusKind.BuyNextOpen when status.Reason == BuyReason.Signal => $"Buy at the open on {Display.Date(status.BuyDate)}.",
            StatusKind.BuyNextOpen => $"Buy at the open on {Display.Date(status.BuyDate)}: the month-end deadline, since no dip signal came.",
            StatusKind.Triggered => $"This month's buy was at the open on {Display.Date(status.BuyDate)}. The next chance starts with {Display.Date(nextMonth)}'s open, or earlier if the month's last close scores {BuyRule.Threshold:0}.",
            StatusKind.Stale => "No date until the prices are current. Refresh first.",
            StatusKind.NotEnoughData => "Not enough price history to score yet.",
            _ => $"Not yet. Buy the next open after a close scores {BuyRule.Threshold:0}+, or at the open on {Display.Date(deadline)} at the latest.",
        };

        var reasons = new List<string>();
        var explained = atSignal ?? latest;
        if (explained is not null)
        {
            var when = atSignal is not null ? $"on {Display.Short(atSignal.Date)}" : "at the last close";
            reasons.Add($"Score {explained.Total:0} {when} (a buy needs {BuyRule.Threshold:0}). What added to it:");
            reasons.AddRange(Indicators(explained));
        }
        if (analysis.Backtest.Months.Count > 1 && analysis.Backtest.Verdict == Verdict.LikelyWorse)
            reasons.Add("Caution: for this ticker, buying on the first trading day has been cheaper on average than waiting for the signal.");
        return (headline, reasons);
    }

    private static IEnumerable<string> Indicators(DayScore s)
    {
        var parts = new List<(double Points, string Text)>
        {
            (s.RsiPoints, $"RSI {s.Rsi:0}: {(s.RsiPoints >= 10 ? "it's been sold off recently" : "not much of a sell-off")}"),
            (s.BollingerPoints, $"price at {s.PercentB * 100:0}% of its Bollinger band: {(s.BollingerPoints >= 10 ? "low for its recent range" : "not low for its recent range")}"),
            (s.MacdPoints, $"MACD {s.MacdHistogram:+0.00;-0.00}: {(s.MacdPoints > 0 ? "the drop is losing steam" : "no sign the drop is fading")}"),
            (s.VolumePoints, $"volume {s.VolumeRatio:0.0}x normal: {(s.VolumePoints > 0 ? "heavy selling that may be wearing out" : "no heavy selling")}"),
            (s.MarketPoints, s.MarketAboveAverage ? "the market (SPY) is above its 200-day average" : "the market (SPY) is below its 200-day average"),
        };
        return parts.OrderByDescending(p => p.Points).Select(p => $"{p.Text} (+{p.Points:0})");
    }
}
