using BuyWindow.Core;

namespace BuyWindow.Web.Market;

/// <summary>The "Own it?" check for a ticker, from Finviz and the SEC filings. Both clients keep their own caches.</summary>
public sealed class OwnService(FinvizClient finviz, SecClient sec, MarketStore store)
{
    public async Task<OwnResult> ForAsync(WatchedTicker ticker, CancellationToken ct = default)
    {
        var finvizTask = finviz.SnapshotAsync(ticker.Symbol, ct);
        var financials = await sec.FinancialsAsync(ticker.Symbol, ct);
        var snapshot = (await finvizTask).Snapshot;

        var price = store.QuoteFor(ticker.Symbol)?.Price
            ?? (ticker.Analysis.Bars.Count > 0 ? ticker.Analysis.Bars[^1].Close : (double?)null);
        var lynch = financials is not null && price is { } p ? LynchScore.For(financials, p) : null;
        return OwnCheck.For(snapshot, lynch);
    }
}
