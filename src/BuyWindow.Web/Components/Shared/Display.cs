using System.Globalization;
using BuyWindow.Core;

namespace BuyWindow.Web.Components.Shared;

/// <summary>The app's wording for statuses and verdicts, shared by every page.</summary>
public static class Display
{
    public static string Date(DateOnly? date) =>
        date?.ToString("MMM d, yyyy", CultureInfo.InvariantCulture) ?? "-";

    public static string Short(DateOnly? date) =>
        date?.ToString("ddd, MMM d", CultureInfo.InvariantCulture) ?? "-";

    public static string Checked(DateTime? eastern) =>
        eastern?.ToString("MMM d, yyyy 'at' h:mm tt 'ET'", CultureInfo.InvariantCulture) ?? "not yet";

    public static string Host(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host : url;

    public static string Percent(double value) =>
        double.IsFinite(value) ? $"{value:+0.0;-0.0;0.0}%" : "n/a";

    public static string Ordinal(int day) => (day % 100, day % 10) switch
    {
        (11 or 12 or 13, _) => $"{day}th",
        (_, 1) => $"{day}st",
        (_, 2) => $"{day}nd",
        (_, 3) => $"{day}rd",
        _ => $"{day}th",
    };

    public static string BestDayLabel(TickerAnalysis analysis) =>
        analysis.Pattern?.BestDay is { } day ? $"Around the {Ordinal(day.ApproxDayOfMonth)}" : "No best day";

    public static string BestDayDetail(TickerAnalysis analysis)
    {
        var pattern = analysis.Pattern;
        if (pattern?.BestDay is null) return "No finished months yet";
        if (analysis.LowConfidence) return $"Could be chance: only {pattern.Months} {(pattern.Months == 1 ? "month" : "months")} of data";
        if (pattern.BestWindow is not { } window) return "Could be chance: no part of the month stands out";
        return $"Cheapest stretch: {Ordinal(window.ApproxFirstDayOfMonth)} to {Ordinal(window.ApproxLastDayOfMonth)}";
    }

    public static string StatusLabel(TickerStatus status) => status.Kind switch
    {
        StatusKind.BuyNextOpen => $"Buy at open {Short(status.BuyDate)}",
        StatusKind.Triggered => $"Triggered {Short(status.SignalDate)}",
        StatusKind.Stale => "Data out of date",
        StatusKind.NotEnoughData => "Not enough data",
        _ => "Wait",
    };

    public static string StatusDetail(TickerStatus status) => status.Kind switch
    {
        StatusKind.BuyNextOpen when status.Reason == BuyReason.Signal => $"Score reached {BuyRule.Threshold:0}",
        StatusKind.BuyNextOpen => "Month-end deadline",
        StatusKind.Triggered => $"Buy date was {Short(status.BuyDate)}",
        StatusKind.Stale when status.DeadlineDue => "Deadline buy is due; refresh to confirm",
        StatusKind.Stale => "No buy signals until data is current",
        StatusKind.NotEnoughData => $"Needs {Scoring.WarmupBars} trading days",
        _ => "No signal yet",
    };

    public static string StatusClass(StatusKind kind) => kind switch
    {
        StatusKind.BuyNextOpen => "status-buy",
        StatusKind.Stale => "status-warn",
        _ => "status-neutral",
    };

    public static string StatusGlyph(StatusKind kind) => kind switch
    {
        StatusKind.BuyNextOpen => "▲",
        StatusKind.Stale => "!",
        StatusKind.Triggered => "✓",
        _ => "·",
    };

    public static string VerdictLabel(Verdict verdict) => verdict switch
    {
        Verdict.LikelyEdge => "Signal better",
        Verdict.LikelyWorse => "First day better",
        _ => "No clear winner",
    };

    public static string VerdictClass(Verdict verdict) => verdict switch
    {
        Verdict.LikelyEdge => "verdict-edge",
        Verdict.LikelyWorse => "verdict-worse",
        _ => "verdict-none",
    };

    public static string VerdictGlyph(Verdict verdict) => verdict switch
    {
        Verdict.LikelyEdge => "▲",
        Verdict.LikelyWorse => "▼",
        _ => "●",
    };

    public static string VerdictSentence(BacktestResult result)
    {
        var n = result.Months.Count;
        if (n == 0) return "There are no finished months to test yet.";

        var size = Math.Abs(result.Edge.Mean).ToString("0.0", CultureInfo.InvariantCulture);
        return result.Verdict switch
        {
            Verdict.LikelyEdge => $"The signal has been better. Over {n} months, it paid {size}% less on average than buying on the first trading day.",
            Verdict.LikelyWorse => $"Buying on the first trading day has been better. Over {n} months, the signal paid {size}% more on average.",
            _ => $"Neither has clearly been better. Over {n} months, the signal's difference from the first trading day could just be chance.",
        };
    }

    public static string VerdictDetail(BacktestResult result) =>
        $"Signal's price compared with the first trading day: {Difference(result.Edge.Mean)} on average. " +
        $"The 95% likely range is {Difference(result.Edge.Low)} to {Difference(result.Edge.High)}.";

    // A positive edge means the signal paid less.
    private static string Difference(double edge)
    {
        if (!double.IsFinite(edge)) return "n/a";
        var size = Math.Abs(edge).ToString("0.0", CultureInfo.InvariantCulture);
        return edge >= 0 ? $"{size}% lower" : $"{size}% higher";
    }
}
