using Microsoft.EntityFrameworkCore;

namespace BuyWindow.Web.Data;

/// <summary>A symbol the app keeps prices for. SPY is tracked for the market trend even when not on the watchlist.</summary>
public class TrackedSymbol
{
    public required string Symbol { get; set; }
    public required string Name { get; set; }
    public bool IsWatched { get; set; }
    public DateTime? LastFetchedUtc { get; set; }
}

public class StoredBar
{
    public required string Symbol { get; set; }
    public DateOnly Date { get; set; }
    public double Open { get; set; }
    public double High { get; set; }
    public double Low { get; set; }
    public double Close { get; set; }
    public double Volume { get; set; }
}

public class AppDb(DbContextOptions<AppDb> options) : DbContext(options)
{
    public DbSet<TrackedSymbol> Symbols => Set<TrackedSymbol>();
    public DbSet<StoredBar> Bars => Set<StoredBar>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<TrackedSymbol>().HasKey(s => s.Symbol);
        model.Entity<StoredBar>().HasKey(b => new { b.Symbol, b.Date });
    }
}
