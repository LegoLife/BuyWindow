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
                ? $"BuyWindow: {fresh[0].Ticker.Symbol} needs a look"
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
            await email.SendAsync("BuyWindow: test email", $"Email alerts are working.\n\n{Link()}\nFidelity login: {FidelityLogin}");
            LastError = null;
            return $"Test email sent to {email.Recipient}.";
        }
        catch (Exception ex)
        {
            LastError = $"Couldn't send the test email: {ex.Message}";
            return LastError;
        }
    }

    private string Compose(List<(WatchedTicker Ticker, TickerAlert? Alert)> items)
    {
        var text = new StringBuilder();
        foreach (var (ticker, alert) in items)
        {
            var analysis = ticker.Analysis;
            text.AppendLine($"{ticker.Symbol} - {ticker.Name}");
            text.AppendLine(alert!.Kind == AlertKind.Buy
                ? $"{Display.StatusLabel(analysis.Status)}. {Display.StatusDetail(analysis.Status)}."
                : "A month-end deadline buy is due, but the price data is out of date. Check the app before buying.");
            text.AppendLine($"Does timing help? {Display.VerdictLabel(analysis.Backtest.Verdict)}, over {analysis.Backtest.Months.Count} months{(analysis.LowConfidence ? " (low confidence)" : "")}.");
            text.AppendLine();
        }
        text.AppendLine(Link());
        return text.ToString();
    }

    private string Link() => config["App:Url"] is { Length: > 0 } url ? $"Open BuyWindow: {url}" : "Open BuyWindow to see the details.";
}
