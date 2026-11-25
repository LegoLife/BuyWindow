using System.Globalization;
using System.Text.Json;
using BuyWindow.Core;

namespace BuyWindow.Web.Market;

/// <summary>The next Fed meetings' odds, soonest first, and why they're missing if they are.</summary>
public sealed record FedOutlook(IReadOnlyList<FedMeeting> Meetings, string? Problem);

/// <summary>
/// Reads Kalshi's public "Fed decision" markets (no account or key needed). Kept for an hour:
/// the odds are context for the day, not a trading feed.
/// </summary>
public sealed class KalshiClient(IHttpClientFactory http, TimeProvider clock, ILogger<KalshiClient> logger)
{
    public const string HttpClientName = "kalshi";
    private const string Url = "trade-api/v2/markets?series_ticker=KXFEDDECISION&status=open&limit=200";
    private static readonly TimeSpan KeepFor = TimeSpan.FromHours(1);
    private static readonly TimeSpan RetryAfter = TimeSpan.FromMinutes(10);

    private readonly SemaphoreSlim _oneAtATime = new(1, 1);
    private (DateTimeOffset At, FedOutlook Outlook)? _cache;

    public async Task<FedOutlook> OutlookAsync(CancellationToken ct = default)
    {
        if (Fresh() is { } cached) return cached;
        await _oneAtATime.WaitAsync(ct);
        try
        {
            if (Fresh() is { } fetchedMeanwhile) return fetchedMeanwhile;
            FedOutlook outlook;
            try
            {
                using var doc = JsonDocument.Parse(await http.CreateClient(HttpClientName).GetStringAsync(Url, ct));
                outlook = new FedOutlook(Parse(doc.RootElement), null);
                if (outlook.Meetings.Count == 0) outlook = outlook with { Problem = "Kalshi has no open Fed markets." };
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException or KeyNotFoundException or InvalidOperationException
                                           || (ex is TaskCanceledException && !ct.IsCancellationRequested))
            {
                logger.LogWarning("Couldn't read Fed odds from Kalshi: {Error}", ex.Message);
                outlook = new FedOutlook([], "Kalshi couldn't be reached.");
            }
            _cache = (clock.GetUtcNow(), outlook);
            return outlook;
        }
        finally
        {
            _oneAtATime.Release();
        }
    }

    private FedOutlook? Fresh() =>
        _cache is { } c && clock.GetUtcNow() - c.At < (c.Outlook.Problem is null ? KeepFor : RetryAfter) ? c.Outlook : null;

    // Markets come as KXFEDDECISION-26DEC-H25, one per outcome; the event ticker groups a meeting.
    private static List<FedMeeting> Parse(JsonElement root) =>
        root.GetProperty("markets").EnumerateArray()
            .GroupBy(m => m.GetProperty("event_ticker").GetString()!)
            .Select(g => FedOdds.Meeting(
                DateOnly.FromDateTime(g.First().GetProperty("close_time").GetDateTime()),
                g.Select(m => new FedContract(
                    m.GetProperty("ticker").GetString()!.Split('-')[^1],
                    Dollars(m, "yes_bid_dollars"), Dollars(m, "yes_ask_dollars"), Dollars(m, "last_price_dollars")))))
            .OfType<FedMeeting>()
            .OrderBy(m => m.Date)
            .ToList();

    private static double? Dollars(JsonElement market, string name) =>
        market.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            && double.TryParse(v.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : null;
}
