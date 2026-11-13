using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using BuyWindow.Core;

namespace BuyWindow.Web.Market;

/// <summary>A Finviz snapshot and when it was fetched, in US Eastern time.</summary>
public sealed record FinvizResult(FinvizSnapshot? Snapshot, DateTime FetchedEastern, string? Problem);

/// <summary>
/// Reads a ticker's Finviz quote page with the Obscura browser (https://github.com/h4ckf0r0day/obscura).
/// It takes the page as served, without running its scripts: the table and headlines are already in it,
/// and Finviz's chart scripts stall Obscura. Pages are kept for an hour and fetched one at a time, so
/// browsing the watchlist stays at a person's pace.
/// </summary>
public sealed class FinvizClient(IConfiguration config, TimeProvider clock, ILogger<FinvizClient> logger)
{
    private static readonly TimeSpan KeepFor = TimeSpan.FromHours(1);
    private static readonly TimeSpan RetryAfter = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    private readonly ConcurrentDictionary<string, (DateTimeOffset At, FinvizResult Result)> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _oneAtATime = new(1, 1);

    private string ObscuraPath => config["Finviz:ObscuraPath"] is { Length: > 0 } path ? path : "obscura";

    public async Task<FinvizResult> SnapshotAsync(string symbol, CancellationToken ct = default)
    {
        if (Fresh(symbol) is { } cached) return cached;

        await _oneAtATime.WaitAsync(ct);
        try
        {
            if (Fresh(symbol) is { } fetchedMeanwhile) return fetchedMeanwhile;

            var now = clock.GetUtcNow();
            var eastern = MarketStore.ToEastern(now);
            FinvizResult result;
            try
            {
                var html = await FetchAsync(FinvizSnapshot.QuoteUrl(symbol), ct);
                var snapshot = FinvizPage.Parse(html, DateOnly.FromDateTime(eastern));
                result = new FinvizResult(snapshot, eastern, snapshot is null ? "Finviz didn't return a quote for this ticker." : null);
            }
            catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or TimeoutException)
            {
                logger.LogWarning("Couldn't read {Symbol} from Finviz: {Error}", symbol, ex.Message);
                result = new FinvizResult(null, eastern, ex is Win32Exception ? "The Obscura browser isn't installed." : "Finviz couldn't be reached.");
            }

            _cache[symbol] = (now, result);
            return result;
        }
        finally
        {
            _oneAtATime.Release();
        }
    }

    private FinvizResult? Fresh(string symbol) =>
        _cache.TryGetValue(symbol, out var hit) && clock.GetUtcNow() - hit.At < (hit.Result.Snapshot is null ? RetryAfter : KeepFor)
            ? hit.Result
            : null;

    // "--dump original" returns the page as served, without the browser running its scripts.
    private async Task<string> FetchAsync(string url, CancellationToken ct)
    {
        var start = new ProcessStartInfo(ObscuraPath)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
        };
        foreach (var arg in new[] { "fetch", url, "--dump", "original", "--timeout", "20", "-q" }) start.ArgumentList.Add(arg);

        using var process = Process.Start(start) ?? throw new InvalidOperationException("Obscura didn't start.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(Timeout);
        try
        {
            var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var errors = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            if (process.ExitCode != 0)
                throw new InvalidOperationException($"Obscura exited with {process.ExitCode}: {Last(await errors)}");
            return await output;
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            if (ct.IsCancellationRequested) throw;
            throw new TimeoutException("Obscura took longer than 30 seconds.");
        }
    }

    private static string Last(string text) =>
        text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault() ?? "";
}
