using BuyWindow.Core;

namespace BuyWindow.Web.Market;

public sealed record TickerMatch(string Symbol, string Name, string AssetType);

/// <summary>The latest traded price. Unlike the daily bars it isn't adjusted, so it's compared with an unadjusted previous close.</summary>
public sealed record LiveQuote(string Symbol, double Price, double? PreviousClose, DateTimeOffset At);

/// <summary>The price provider seam: swapping providers means implementing this and nothing else.</summary>
public interface IMarketData
{
    bool IsConfigured { get; }
    Task<IReadOnlyList<TickerMatch>> SearchAsync(string query, CancellationToken ct = default);
    Task<IReadOnlyList<PriceBar>> GetDailyBarsAsync(string symbol, DateOnly start, CancellationToken ct = default);

    /// <summary>Latest prices for several symbols in one request. Symbols without a price are left out.</summary>
    Task<IReadOnlyList<LiveQuote>> GetQuotesAsync(IReadOnlyCollection<string> symbols, CancellationToken ct = default);
}

public sealed class MarketDataException(string message, Exception? inner = null) : Exception(message, inner);
