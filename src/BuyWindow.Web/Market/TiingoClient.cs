using System.Net;
using System.Net.Http.Headers;
using System.Text.Json.Serialization;
using BuyWindow.Core;

namespace BuyWindow.Web.Market;

public sealed class TiingoClient(HttpClient http, IConfiguration config) : IMarketData
{
    private static readonly string[] SupportedAssetTypes = ["Stock", "ETF"];

    private readonly string? _apiKey = config["Tiingo:ApiKey"];

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_apiKey);

    public async Task<IReadOnlyList<TickerMatch>> SearchAsync(string query, CancellationToken ct = default)
    {
        var results = await GetAsync<List<SearchResult>>(
            $"tiingo/utilities/search?query={Uri.EscapeDataString(query)}", ct);

        return results
            .Where(r => r.IsActive && SupportedAssetTypes.Contains(r.AssetType, StringComparer.OrdinalIgnoreCase))
            .Select(r => new TickerMatch(r.Ticker.ToUpperInvariant(), r.Name ?? r.Ticker, r.AssetType!))
            .DistinctBy(m => m.Symbol) // Tiingo can list one symbol more than once
            .ToList();
    }

    public async Task<IReadOnlyList<PriceBar>> GetDailyBarsAsync(string symbol, DateOnly start, CancellationToken ct = default)
    {
        var rows = await GetAsync<List<PriceRow>>(
            $"tiingo/daily/{Uri.EscapeDataString(symbol)}/prices?startDate={start:yyyy-MM-dd}", ct);

        return rows
            .Select(r => new PriceBar(DateOnly.FromDateTime(r.Date), r.AdjOpen, r.AdjHigh, r.AdjLow, r.AdjClose, r.AdjVolume))
            .ToList();
    }

    private async Task<T> GetAsync<T>(string path, CancellationToken ct)
    {
        if (!IsConfigured)
            throw new MarketDataException("No Tiingo API key is set.");

        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Token", _apiKey);

        try
        {
            using var response = await http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
                throw new MarketDataException(Describe(response.StatusCode));

            return await response.Content.ReadFromJsonAsync<T>(ct)
                ?? throw new MarketDataException("Tiingo returned an empty response.");
        }
        catch (HttpRequestException ex)
        {
            throw new MarketDataException("Couldn't reach Tiingo.", ex);
        }
        catch (System.Text.Json.JsonException ex)
        {
            throw new MarketDataException("Tiingo returned a response this app couldn't read.", ex);
        }
    }

    private static string Describe(HttpStatusCode status) => status switch
    {
        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "Tiingo rejected the API key.",
        HttpStatusCode.NotFound => "Tiingo has no prices for that ticker.",
        HttpStatusCode.TooManyRequests => "Tiingo's free-plan limit was reached (50 requests an hour). The app will retry.",
        _ => $"Tiingo returned an error ({(int)status}).",
    };

    private sealed record SearchResult(
        [property: JsonPropertyName("ticker")] string Ticker,
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("assetType")] string? AssetType,
        [property: JsonPropertyName("isActive")] bool IsActive);

    private sealed record PriceRow(
        [property: JsonPropertyName("date")] DateTime Date,
        [property: JsonPropertyName("adjOpen")] double AdjOpen,
        [property: JsonPropertyName("adjHigh")] double AdjHigh,
        [property: JsonPropertyName("adjLow")] double AdjLow,
        [property: JsonPropertyName("adjClose")] double AdjClose,
        [property: JsonPropertyName("adjVolume")] double AdjVolume);
}
