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

    public static string Percent(double value) =>
        double.IsFinite(value) ? $"{value:+0.0;-0.0;0.0}%" : "n/a";

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
        Verdict.LikelyEdge => "Likely edge",
        Verdict.LikelyWorse => "Likely worse",
        _ => "No clear edge",
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
            Verdict.LikelyEdge => $"Over {n} months, following the signal paid {size}% less on average than buying on the first trading day.",
            Verdict.LikelyWorse => $"Over {n} months, following the signal paid {size}% more on average than buying on the first trading day. Buying on your usual day has been better.",
            _ => $"Over {n} months, the signal's difference from buying on the first trading day is within the noise. There's no evidence it helps, so your usual day is fine.",
        };
    }
}
