using System.Globalization;
using System.Text.Json;

namespace BuyWindow.Web.Market;

/// <summary>
/// Reads economists' consensus forecasts from Forex Factory's free weekly calendar feed. It only covers
/// the current week, so a release's consensus appears the week it comes out. The feed asks callers to
/// go easy, so it's kept for an hour.
/// </summary>
public sealed class ConsensusClient(IHttpClientFactory http, TimeProvider clock, ILogger<ConsensusClient> logger)
{
    public const string HttpClientName = "forexfactory";
    private const string Url = "https://nfs.faireconomy.media/ff_calendar_thisweek.json";
    private static readonly TimeSpan KeepFor = TimeSpan.FromHours(1);
    private static readonly TimeSpan RetryAfter = TimeSpan.FromMinutes(15);

    private sealed record Release(string Title, DateOnly Date, double Forecast);

    private readonly SemaphoreSlim _oneAtATime = new(1, 1);
    private (DateTimeOffset At, IReadOnlyList<Release> Releases, bool Failed)? _cache;

    /// <summary>The US year-over-year CPI consensus for a release on or within two days of this date, if published.</summary>
    public async Task<double?> CpiYoYAsync(DateOnly releaseDate, CancellationToken ct = default) =>
        (await ReleasesAsync(ct))
            .Where(r => r.Title == "CPI y/y" && Math.Abs(r.Date.DayNumber - releaseDate.DayNumber) <= 2)
            .Select(r => (double?)r.Forecast)
            .FirstOrDefault();

    private async Task<IReadOnlyList<Release>> ReleasesAsync(CancellationToken ct)
    {
        if (Fresh() is { } cached) return cached;
        await _oneAtATime.WaitAsync(ct);
        try
        {
            if (Fresh() is { } fetchedMeanwhile) return fetchedMeanwhile;
            List<Release>? releases = null;
            try
            {
                using var doc = JsonDocument.Parse(await http.CreateClient(HttpClientName).GetStringAsync(Url, ct));
                releases = doc.RootElement.EnumerateArray()
                    .Where(e => e.TryGetProperty("country", out var c) && c.GetString() == "USD")
                    .Select(Parse).OfType<Release>().ToList();
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidOperationException
                                           || (ex is TaskCanceledException && !ct.IsCancellationRequested))
            {
                logger.LogWarning("Couldn't read the consensus calendar: {Error}", ex.Message);
            }
            _cache = (clock.GetUtcNow(), releases ?? (IReadOnlyList<Release>)[], releases is null);
            return _cache.Value.Releases;
        }
        finally
        {
            _oneAtATime.Release();
        }
    }

    private IReadOnlyList<Release>? Fresh() =>
        _cache is { } c && clock.GetUtcNow() - c.At < (c.Failed ? RetryAfter : KeepFor) ? c.Releases : null;

    // {"title":"CPI y/y","country":"USD","date":"2026-12-10T08:30:00-05:00","forecast":"3.6%",...}
    private static Release? Parse(JsonElement e)
    {
        if (!e.TryGetProperty("title", out var t) || !e.TryGetProperty("date", out var d) || !e.TryGetProperty("forecast", out var f))
            return null;
        var forecast = f.GetString()?.TrimEnd('%');
        if (!double.TryParse(forecast, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)) return null;
        if (!DateTimeOffset.TryParse(d.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) return null;
        return new Release(t.GetString() ?? "", DateOnly.FromDateTime(date.DateTime), value);
    }
}
