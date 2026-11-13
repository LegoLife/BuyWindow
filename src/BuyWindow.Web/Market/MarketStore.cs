using BuyWindow.Core;
using BuyWindow.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace BuyWindow.Web.Market;

public sealed record WatchedTicker(string Symbol, string Name, TickerAnalysis Analysis);

/// <summary>Keeps stored prices current and turns them into analyses for the pages.</summary>
public sealed class MarketStore(IDbContextFactory<AppDb> dbs, IMarketData provider, TimeProvider clock, ILogger<MarketStore> logger)
{
    public const string MarketSymbol = "SPY";

    // A year beyond the ten-year window, so the market's 200-day average exists from day one.
    private const int FetchYears = HistoryWindow.MaxYears + 1;

    private static readonly TimeZoneInfo Eastern = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");

    private readonly SemaphoreSlim _gate = new(1, 1);

    // Kept in memory only: after a restart the first check fetches them again.
    private volatile IReadOnlyDictionary<string, LiveQuote> _quotes = new Dictionary<string, LiveQuote>();
    private bool _quotesFetched;

    public event Action? Changed;

    public LiveQuote? QuoteFor(string symbol) => _quotes.GetValueOrDefault(symbol);

    /// <summary>
    /// Fetches the watchlist's latest prices in one request while the market is open, and once after
    /// a restart. Failures only leave the old prices, because the daily closes still work without them.
    /// </summary>
    public async Task RefreshQuotesAsync(bool force = false, CancellationToken ct = default)
    {
        if (!force && _quotesFetched && !MarketCalendar.QuotesMoving(EasternNow)) return;

        try
        {
            await using var db = await dbs.CreateDbContextAsync(ct);
            var symbols = await db.Symbols.Where(s => s.IsWatched).Select(s => s.Symbol).ToListAsync(ct);
            var quotes = await provider.GetQuotesAsync(symbols, ct);
            _quotes = quotes.ToDictionary(q => q.Symbol);
            _quotesFetched = true;
        }
        catch (MarketDataException ex)
        {
            logger.LogWarning("Live price fetch failed: {Error}", ex.Message);
            return;
        }
        Changed?.Invoke();
    }

    public string? LastError { get; private set; }

    /// <summary>When the background check last finished, in Eastern time. Empty until the first one after startup.</summary>
    public DateTime? LastCheckedEastern { get; private set; }

    public bool IsConfigured => provider.IsConfigured;

    public DateTime EasternNow => ToEastern(clock.GetUtcNow());

    public static DateTime ToEastern(DateTimeOffset at) => TimeZoneInfo.ConvertTimeFromUtc(at.UtcDateTime, Eastern);

    public void MarkChecked()
    {
        LastCheckedEastern = EasternNow;
        Changed?.Invoke();
    }

    public Task<IReadOnlyList<TickerMatch>> SearchAsync(string query, CancellationToken ct = default) =>
        provider.SearchAsync(query, ct);

    public async Task AddAsync(TickerMatch match, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            await using var db = await dbs.CreateDbContextAsync(ct);
            var bars = await provider.GetDailyBarsAsync(match.Symbol, FetchStart(), ct);
            if (bars.Count == 0)
                throw new MarketDataException($"Tiingo has no prices for {match.Symbol}.");

            await ReplaceBarsAsync(db, match.Symbol, match.Name, bars, watched: true, ct);
            await EnsureMarketAsync(db, ct);
        }
        finally
        {
            _gate.Release();
        }
        Changed?.Invoke();
    }

    public async Task RemoveAsync(string symbol, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            await using var db = await dbs.CreateDbContextAsync(ct);
            await db.Bars.Where(b => b.Symbol == symbol && symbol != MarketSymbol).ExecuteDeleteAsync(ct);
            await db.Symbols.Where(s => s.Symbol == symbol && symbol != MarketSymbol).ExecuteDeleteAsync(ct);
            await db.Symbols.Where(s => s.Symbol == symbol)
                .ExecuteUpdateAsync(u => u.SetProperty(s => s.IsWatched, false), ct);
            logger.LogInformation("Removed {Symbol} from the watchlist", symbol);
        }
        finally
        {
            _gate.Release();
        }
        Changed?.Invoke();
    }

    /// <summary>
    /// Fetches symbols not refreshed since the latest scheduled point. Whole histories are replaced
    /// because a new split or dividend changes every earlier adjusted price.
    /// </summary>
    public async Task RefreshDueAsync(bool force = false, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        var updated = false;
        try
        {
            await using var db = await dbs.CreateDbContextAsync(ct);
            await EnsureMarketAsync(db, ct);

            var due = TimeZoneInfo.ConvertTimeToUtc(MarketCalendar.LatestRefreshPoint(EasternNow), Eastern);
            var symbols = await db.Symbols.ToListAsync(ct);

            foreach (var symbol in symbols.Where(s => force || s.LastFetchedUtc is null || s.LastFetchedUtc < due))
            {
                var bars = await provider.GetDailyBarsAsync(symbol.Symbol, FetchStart(), ct);
                await ReplaceBarsAsync(db, symbol.Symbol, symbol.Name, bars, symbol.IsWatched, ct);
                updated = true;
            }
            LastError = null;
        }
        catch (MarketDataException ex)
        {
            LastError = ex.Message;
            updated = true;
        }
        finally
        {
            _gate.Release();
        }
        if (updated) Changed?.Invoke();
    }

    public async Task<IReadOnlyList<WatchedTicker>> LoadWatchlistAsync(CancellationToken ct = default)
    {
        await using var db = await dbs.CreateDbContextAsync(ct);
        var symbols = await db.Symbols.Where(s => s.IsWatched).OrderBy(s => s.Symbol).ToListAsync(ct);
        var market = await LoadBarsAsync(db, MarketSymbol, ct);
        var now = EasternNow;

        var result = new List<WatchedTicker>();
        foreach (var symbol in symbols)
        {
            var bars = await LoadBarsAsync(db, symbol.Symbol, ct);
            result.Add(new WatchedTicker(symbol.Symbol, symbol.Name, await Task.Run(() => Analyzer.Analyze(bars, market, now), ct)));
        }
        return result;
    }

    public async Task<WatchedTicker?> LoadAsync(string symbol, CancellationToken ct = default)
    {
        await using var db = await dbs.CreateDbContextAsync(ct);
        var row = await db.Symbols.FirstOrDefaultAsync(s => s.Symbol == symbol && s.IsWatched, ct);
        if (row is null) return null;

        var bars = await LoadBarsAsync(db, symbol, ct);
        var market = await LoadBarsAsync(db, MarketSymbol, ct);
        var now = EasternNow;
        return new WatchedTicker(symbol, row.Name, await Task.Run(() => Analyzer.Analyze(bars, market, now), ct));
    }

    private DateOnly FetchStart() => DateOnly.FromDateTime(EasternNow).AddYears(-FetchYears);

    private async Task EnsureMarketAsync(AppDb db, CancellationToken ct)
    {
        if (await db.Symbols.AnyAsync(s => s.Symbol == MarketSymbol, ct)) return;

        var bars = await provider.GetDailyBarsAsync(MarketSymbol, FetchStart(), ct);
        await ReplaceBarsAsync(db, MarketSymbol, "SPDR S&P 500 ETF", bars, watched: false, ct);
    }

    private async Task ReplaceBarsAsync(AppDb db, string symbol, string name, IReadOnlyList<PriceBar> bars, bool watched, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await db.Bars.Where(b => b.Symbol == symbol).ExecuteDeleteAsync(ct);
        db.Bars.AddRange(bars.Select(b => new StoredBar
        {
            Symbol = symbol, Date = b.Date, Open = b.Open, High = b.High, Low = b.Low, Close = b.Close, Volume = b.Volume,
        }));

        var row = await db.Symbols.FindAsync([symbol], ct);
        if (row is null)
        {
            row = new TrackedSymbol { Symbol = symbol, Name = name };
            db.Symbols.Add(row);
        }
        row.Name = name;
        row.IsWatched = watched;
        row.LastFetchedUtc = clock.GetUtcNow().UtcDateTime;

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        db.ChangeTracker.Clear();
    }

    private static async Task<List<PriceBar>> LoadBarsAsync(AppDb db, string symbol, CancellationToken ct) =>
        await db.Bars.AsNoTracking()
            .Where(b => b.Symbol == symbol)
            .OrderBy(b => b.Date)
            .Select(b => new PriceBar(b.Date, b.Open, b.High, b.Low, b.Close, b.Volume))
            .ToListAsync(ct);
}
