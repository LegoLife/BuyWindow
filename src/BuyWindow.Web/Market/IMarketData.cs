using BuyWindow.Core;

namespace BuyWindow.Web.Market;

public sealed record TickerMatch(string Symbol, string Name, string AssetType);

/// <summary>The price provider seam: swapping providers means implementing this and nothing else.</summary>
public interface IMarketData
{
    bool IsConfigured { get; }
    Task<IReadOnlyList<TickerMatch>> SearchAsync(string query, CancellationToken ct = default);
    Task<IReadOnlyList<PriceBar>> GetDailyBarsAsync(string symbol, DateOnly start, CancellationToken ct = default);
}

public sealed class MarketDataException(string message, Exception? inner = null) : Exception(message, inner);
