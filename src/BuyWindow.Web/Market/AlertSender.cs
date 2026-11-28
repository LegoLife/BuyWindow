using System.Text;
using BuyWindow.Core;
using BuyWindow.Web.Components.Shared;
using BuyWindow.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace BuyWindow.Web.Market;

/// <summary>Emails one digest of anything new on the watchlist, and remembers what was sent.</summary>
public sealed class AlertSender(
    IDbContextFactory<AppDb> dbs,
    MarketStore store,
    EmailSender email,
    IConfiguration config,
    TimeProvider clock,
    ILogger<AlertSender> logger)
{
    private const string FidelityLogin = "https://digital.fidelity.com/prgw/digital/signin/retail";

    public string? LastError { get; private set; }

    public async Task SendDueAsync(CancellationToken ct = default)
    {
        if (!email.IsConfigured) return;

        var now = store.EasternNow;
        var due = (await store.LoadWatchlistAsync(ct))
            .Select(ticker => (Ticker: ticker, Alert: AlertRule.For(ticker.Analysis.Status, now)))
            .Where(item => item.Alert is not null)
            // A failed refresh means a Buy could rest on uncorrected numbers. Deadline warnings are about that very failure.
            .Where(item => store.LastError is null || item.Alert!.Kind == AlertKind.DeadlineBlocked)
            .ToList();
        if (due.Count == 0) return;

        await using var db = await dbs.CreateDbContextAsync(ct);
        var sent = (await db.SentAlerts.ToListAsync(ct)).Select(a => (a.Symbol, a.Key)).ToHashSet();
        var fresh = due.Where(item => !sent.Contains((item.Ticker.Symbol, item.Alert!.Key))).ToList();
        if (fresh.Count == 0) return;

        try
        {
            var subject = fresh.Count == 1
                ? $"BuyWindow: {fresh[0].Ticker.Symbol} needs a look ({ReasonTag(fresh[0].Alert!, fresh[0].Ticker.Analysis)})"
                : $"BuyWindow: {fresh.Count} tickers need a look";
            await email.SendAsync(subject, Compose(fresh), ct);
            LastError = null;
            logger.LogInformation("Emailed alerts for {Symbols}", string.Join(", ", fresh.Select(item => item.Ticker.Symbol)));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Nothing is recorded, so the next check tries again.
            LastError = $"Couldn't send the alert email: {ex.Message}";
            logger.LogWarning("{Error}", LastError);
            return;
        }

        var sentUtc = clock.GetUtcNow().UtcDateTime;
        db.SentAlerts.AddRange(fresh.Select(item => new SentAlert { Symbol = item.Ticker.Symbol, Key = item.Alert!.Key, SentUtc = sentUtc }));
        await db.SaveChangesAsync(ct);
    }

    public async Task<string> SendTestAsync()
    {
        try
        {
            await email.SendAsync("BuyWindow: test email", $"Email alerts are working.\n\n{await SampleAsync()}{Link()}\nFidelity login: {FidelityLogin}");
            LastError = null;
            return $"Test email sent to {email.Recipient}.";
        }
        catch (Exception ex)
        {
            LastError = $"Couldn't send the test email: {ex.Message}";
            return LastError;
        }
    }

    /// <summary>A buy alert as it would be written for one watchlist ticker, preferring one that's actually due. Nothing is recorded as sent.</summary>
    private async Task<string> SampleAsync()
    {
        var watchlist = await store.LoadWatchlistAsync();
        var ticker = watchlist.FirstOrDefault(t => t.Analysis.Status.Kind == StatusKind.BuyNextOpen) ?? watchlist.FirstOrDefault();
        if (ticker is null) return "";
        var due = ticker.Analysis.Status.Kind == StatusKind.BuyNextOpen;
        var header = due
            ? $"--- Sample buy alert (this is what {ticker.Symbol}'s alert looks like) ---"
            : $"--- Sample buy alert ({ticker.Symbol} isn't actually due; this only shows the format) ---";
        return $"{header}\n\n{Compose([(ticker, new TickerAlert(AlertKind.Buy, "sample"))], withLink: false)}---\n\n";
    }

    private string Compose(List<(WatchedTicker Ticker, TickerAlert? Alert)> items, bool withLink = true)
    {
        var text = new StringBuilder();
        foreach (var (ticker, alert) in items)
        {
            var analysis = ticker.Analysis;
            text.AppendLine($"{ticker.Symbol} - {ticker.Name}");
            text.AppendLine(alert!.Kind == AlertKind.Buy
                ? $"{Display.StatusLabel(analysis.Status)}. {Display.StatusDetail(analysis.Status)}."
                : "A month-end deadline buy is due, but the price data is out of date. Check the app before buying.");
            if (alert.Kind == AlertKind.Buy) text.AppendLine(Why(analysis));
            text.AppendLine($"Signal or first day? {Display.VerdictLabel(analysis.Backtest.Verdict)}, over {analysis.Backtest.Months.Count} months{(analysis.LowConfidence ? " (low confidence)" : "")}.");
            text.AppendLine();
        }
        if (withLink)
        {
            text.AppendLine(Link());
            text.AppendLine($"Fidelity login: {FidelityLogin}");
        }
        return text.ToString();
    }

    private static string Why(TickerAnalysis analysis)
    {
        var status = analysis.Status;
        if (status.Reason == BuyReason.Signal)
        {
            var signal = analysis.Scores.FirstOrDefault(s => s.Date == status.SignalDate);
            return $"Why: score signal. The score hit {signal?.Total ?? BuyRule.Threshold:0} on {status.SignalDate:M/d}, at or above the {BuyRule.Threshold:0} threshold.";
        }
        var latest = analysis.Scores.LastOrDefault();
        var best = analysis.Scores.Where(s => status.BuyDate is { } d && s.Date.Year == d.Year && s.Date.Month == d.Month && s.Date < d)
            .MaxBy(s => s.Total);
        var facts = new List<string>();
        if (best is not null) facts.Add($"best {best.Total:0} on {best.Date:M/d}");
        if (latest is not null) facts.Add($"latest {latest.Total:0}");
        var detail = facts.Count > 0 ? $" ({string.Join(", ", facts)})" : "";
        return $"Why: month-end deadline, not the score. The score never reached {BuyRule.Threshold:0} this month{detail}, so the rule buys on the last trading day anyway.";
    }

    private static string ReasonTag(TickerAlert alert, TickerAnalysis analysis) =>
        alert.Kind != AlertKind.Buy ? "deadline, data stale"
        : analysis.Status.Reason == BuyReason.Signal ? "score signal" : "month-end deadline";

    private string Link() => config["App:Url"] is { Length: > 0 } url ? $"Open BuyWindow: {url}" : "Open BuyWindow to see the details.";
}
