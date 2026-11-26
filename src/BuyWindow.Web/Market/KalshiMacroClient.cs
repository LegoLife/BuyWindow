using System.Globalization;
using System.Text.Json;
using BuyWindow.Core;

namespace BuyWindow.Web.Market;

/// <summary>
/// One stock-moving market, ready to show: "CPI, out Dec 10: 3.7% YoY", and why it matters. Figures
/// reported in steps also carry their event ticker and ladder, so a consensus can be compared to them.
/// </summary>
public sealed record MacroLine(string Label, string Value, string Why,
    string? EventTicker = null, IReadOnlyList<RangeContract>? Ladder = null, double Step = 0, DateOnly? Closes = null)
{
    public bool IsCpi => EventTicker?.StartsWith("KXCPIYOY-", StringComparison.Ordinal) == true;
}

/// <summary>
/// Reads Kalshi's busiest stock-moving markets beyond the Fed: recession, inflation, jobs and the
/// S&amp;P 500 at year end. Public, no key; kept for an hour like the Fed odds.
/// </summary>
public sealed class KalshiMacroClient(IHttpClientFactory http, TimeProvider clock, ILogger<KalshiMacroClient> logger)
{
    private static readonly TimeSpan KeepFor = TimeSpan.FromHours(1);
    private static readonly TimeSpan RetryAfter = TimeSpan.FromMinutes(10);

    private enum Kind { Chance, Number }

    private sealed record Series(string Ticker, string Label, Kind Kind, string Format, string Why, double Step = 0);

    // Picked by trading volume among markets that move stocks (checked 2026-11-25).
    private static readonly Series[] All =
    [
        new("KXRECSSNBER", "Recession", Kind.Chance, "", "A recession usually means falling earnings and a falling market."),
        new("KXCPIYOY", "CPI, out {0}", Kind.Number, "0.0'% YoY'", "Hot inflation makes rate cuts less likely, which weighs on stocks.", 0.1),
        new("KXU3", "Unemployment, out {0}", Kind.Number, "0.0'%'", "Rising unemployment points to a slowing economy.", 0.1),
        new("KXINXY", "S&P 500 on {0}", Kind.Number, "#,0", "Where the market's bettors expect the index to finish."),
    ];

    private readonly SemaphoreSlim _oneAtATime = new(1, 1);
    private (DateTimeOffset At, IReadOnlyList<MacroLine> Lines, bool Failed)? _cache;

    public async Task<IReadOnlyList<MacroLine>> LinesAsync(CancellationToken ct = default)
    {
        if (Fresh() is { } cached) return cached;
        await _oneAtATime.WaitAsync(ct);
        try
        {
            if (Fresh() is { } fetchedMeanwhile) return fetchedMeanwhile;
            var results = await Task.WhenAll(All.Select(s => ReadAsync(s, ct)));
            var lines = results.SelectMany(r => r ?? []).ToList();
            _cache = (clock.GetUtcNow(), lines, results.Any(r => r is null));
            return lines;
        }
        finally
        {
            _oneAtATime.Release();
        }
    }

    private IReadOnlyList<MacroLine>? Fresh() =>
        _cache is { } c && clock.GetUtcNow() - c.At < (c.Failed ? RetryAfter : KeepFor) ? c.Lines : null;

    // Null when the series couldn't be read, so the cache retries sooner.
    private async Task<List<MacroLine>?> ReadAsync(Series s, CancellationToken ct)
    {
        try
        {
            var url = $"trade-api/v2/markets?series_ticker={s.Ticker}&status=open&limit=200";
            using var doc = JsonDocument.Parse(await http.CreateClient(KalshiClient.HttpClientName).GetStringAsync(url, ct));
            var events = doc.RootElement.GetProperty("markets").EnumerateArray()
                .GroupBy(m => m.GetProperty("event_ticker").GetString()!)
                .Select(g => (Closes: g.Min(m => m.GetProperty("close_time").GetDateTime()), Markets: g.ToList()))
                .OrderBy(e => e.Closes)
                .ToList();
            return s.Kind == Kind.Chance ? Chances(s, events) : Number(s, events);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or KeyNotFoundException or InvalidOperationException
                                       || (ex is TaskCanceledException && !ct.IsCancellationRequested))
        {
            logger.LogWarning("Couldn't read {Series} from Kalshi: {Error}", s.Ticker, ex.Message);
            return null;
        }
    }

    // Yes/no markets, one per year: "Recession 2027: 23%".
    private static List<MacroLine> Chances(Series s, List<(DateTime Closes, List<JsonElement> Markets)> events) =>
        events.SelectMany(e => e.Markets)
            .Select(m => (Name: m.GetProperty("ticker").GetString()!.Split('-')[^1],
                          Price: FedOdds.Price(new FedContract("", Dollars(m, "yes_bid_dollars"), Dollars(m, "yes_ask_dollars"), Dollars(m, "last_price_dollars")))))
            .Where(m => m.Price is not null)
            .Select(m => new MacroLine($"{s.Label} in 20{m.Name}", $"{m.Price * 100:0}%", s.Why))
            .ToList();

    // Number markets: the soonest event's guess. Stepped figures show their likeliest print,
    // the rest their median. "CPI, out Dec 10: 3.7% YoY".
    private static List<MacroLine> Number(Series s, List<(DateTime Closes, List<JsonElement> Markets)> events)
    {
        foreach (var e in events)
        {
            var contracts = e.Markets.Select(Range).OfType<RangeContract>().ToList();
            var guess = s.Step > 0 ? MacroOdds.LikeliestPrint(contracts, s.Step) : MacroOdds.Median(contracts);
            if (guess is not { } value) continue;
            var label = string.Format(CultureInfo.InvariantCulture, s.Label, e.Closes.ToString("MMM d", CultureInfo.InvariantCulture));
            return [new MacroLine(label, value.ToString(s.Format, CultureInfo.InvariantCulture), s.Why,
                e.Markets[0].GetProperty("event_ticker").GetString(), contracts, s.Step, DateOnly.FromDateTime(e.Closes))];
        }
        return [];
    }

    private static RangeContract? Range(JsonElement m)
    {
        var floor = Number(m, "floor_strike");
        var cap = Number(m, "cap_strike");
        var (low, high) = m.TryGetProperty("strike_type", out var t) ? t.GetString() switch
        {
            "greater" or "greater_or_equal" => (floor, (double?)null),
            "less" or "less_or_equal" => ((double?)null, cap),
            "between" => (floor, cap),
            _ => (null, null),
        } : (null, null);
        if (low is null && high is null) return null;
        return new RangeContract(low, high, Dollars(m, "yes_bid_dollars"), Dollars(m, "yes_ask_dollars"), Dollars(m, "last_price_dollars"));
    }

    private static double? Number(JsonElement m, string name) =>
        m.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;

    private static double? Dollars(JsonElement market, string name) =>
        market.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            && double.TryParse(v.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : null;
}
