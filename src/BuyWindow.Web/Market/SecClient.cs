using System.Collections.Concurrent;
using System.Text.Json.Serialization;
using BuyWindow.Core;

namespace BuyWindow.Web.Market;

/// <summary>
/// An earnings release filed with the SEC: an 8-K reporting results (item 2.02). <see cref="Url"/> is the
/// press release when it can be found, otherwise the filing. <see cref="CallUrl"/> is where the release
/// says the call's webcast is, usually the investor site.
/// </summary>
public sealed record EarningsRelease(DateOnly Filed, string Url, string? CallUrl);

/// <summary>
/// Finds a company's latest earnings release on SEC EDGAR. The SEC asks every app to name a contact
/// email in its User-Agent, so this stays off without one.
/// </summary>
public sealed class SecClient(IHttpClientFactory httpClients, IConfiguration config, TimeProvider clock, ILogger<SecClient> logger)
{
    public const string HttpClientName = "sec";

    private static readonly TimeSpan KeepFor = TimeSpan.FromHours(6);

    private readonly ConcurrentDictionary<string, (DateTimeOffset At, EarningsRelease? Release)> _releases = new(StringComparer.OrdinalIgnoreCase);
    private (DateTimeOffset At, IReadOnlyDictionary<string, long> Ciks)? _ciks;

    private string Contact =>
        new[] { config["Sec:ContactEmail"], config["Smtp:AlertRecipient"], config["Smtp:User"] }
            .FirstOrDefault(e => !string.IsNullOrWhiteSpace(e)) ?? "";

    public bool IsConfigured => Contact.Length > 0;

    /// <summary>Null when there's no contact email, the SEC can't be reached, or the company files no 8-K earnings releases (ETFs, most foreign companies).</summary>
    public async Task<EarningsRelease?> LatestEarningsAsync(string symbol, CancellationToken ct = default)
    {
        if (!IsConfigured) return null;

        var now = clock.GetUtcNow();
        if (_releases.TryGetValue(symbol, out var cached) && now - cached.At < KeepFor) return cached.Release;

        try
        {
            var ciks = await CiksAsync(now, ct);
            if (!ciks.TryGetValue(symbol, out var cik))
            {
                _releases[symbol] = (now, null);
                return null;
            }

            var submissions = await GetAsync<Submissions>($"https://data.sec.gov/submissions/CIK{cik:D10}.json", ct);
            var recent = submissions.Filings.Recent;
            EarningsRelease? release = null;
            for (var i = 0; i < recent.Form.Count; i++)
            {
                if (recent.Form[i] != "8-K" || !recent.Items[i].Split(',').Contains("2.02")) continue;

                release = await ReleaseAsync(cik, recent.AccessionNumber[i],
                    DateOnly.Parse(recent.FilingDate[i], System.Globalization.CultureInfo.InvariantCulture), ct);
                break;
            }

            _releases[symbol] = (now, release);
            return release;
        }
        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            logger.LogWarning("Couldn't read {Symbol}'s SEC filings: {Error}", symbol, ex.Message);
            return null; // not cached, so the next visit tries again
        }
    }

    private readonly ConcurrentDictionary<string, (DateTimeOffset At, Financials? Financials)> _financials = new(StringComparer.OrdinalIgnoreCase);

    // The first concept a company files wins, since companies tag the same thing differently.
    private static readonly string[] EpsConcepts = ["EarningsPerShareDiluted", "EarningsPerShareBasicAndDiluted", "EarningsPerShareBasic"];
    private static readonly string[] ShareConcepts = ["WeightedAverageNumberOfDilutedSharesOutstanding", "WeightedAverageNumberOfSharesOutstandingBasic"];
    private static readonly string[] EquityConcepts = ["StockholdersEquity", "StockholdersEquityIncludingPortionAttributableToNoncontrollingInterest"];
    private static readonly string[] DebtConcepts = ["LongTermDebt", "LongTermDebtNoncurrent", "LongTermDebtCurrent", "DebtCurrent", "ShortTermBorrowings", "CommercialPaper",
        "LongTermDebtAndCapitalLeaseObligationsIncludingCurrentMaturities", "LongTermDebtAndCapitalLeaseObligations", "LongTermDebtAndCapitalLeaseObligationsCurrent", "NotesAndLoansPayableCurrent"];

    /// <summary>
    /// The earnings, share count, equity and debt from the company's 10-K and 10-Q filings. Null without a
    /// contact email, when the SEC can't be reached, or for companies that don't file US GAAP numbers (ETFs, most foreign companies).
    /// </summary>
    public async Task<Financials?> FinancialsAsync(string symbol, CancellationToken ct = default)
    {
        if (!IsConfigured) return null;

        var now = clock.GetUtcNow();
        if (_financials.TryGetValue(symbol, out var cached) && now - cached.At < KeepFor) return cached.Financials;

        try
        {
            Financials? financials = null;
            if ((await CiksAsync(now, ct)).TryGetValue(symbol, out var cik))
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, $"https://data.sec.gov/api/xbrl/companyfacts/CIK{cik:D10}.json");
                request.Headers.TryAddWithoutValidation("User-Agent", $"BuyWindow personal watchlist {Contact}");
                using var response = await httpClients.CreateClient(HttpClientName).SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
                if (response.StatusCode != System.Net.HttpStatusCode.NotFound) // no XBRL filings
                {
                    response.EnsureSuccessStatusCode();
                    await using var stream = await response.Content.ReadAsStreamAsync(ct);
                    using var json = await System.Text.Json.JsonDocument.ParseAsync(stream, cancellationToken: ct);
                    financials = ReadFinancials(json.RootElement);
                }
            }

            _financials[symbol] = (now, financials);
            return financials;
        }
        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            logger.LogWarning("Couldn't read {Symbol}'s SEC financials: {Error}", symbol, ex.Message);
            return null; // not cached, so the next visit tries again
        }
    }

    private static Financials? ReadFinancials(System.Text.Json.JsonElement root)
    {
        if (!root.TryGetProperty("facts", out var facts) || !facts.TryGetProperty("us-gaap", out var gaap)) return null;

        IReadOnlyList<ReportedValue> First(string[] concepts, string unit) =>
            concepts.Select(c => Values(gaap, c, unit)).FirstOrDefault(v => v.Count > 0) ?? [];

        var financials = new Financials(
            First(EpsConcepts, "USD/shares"),
            First(ShareConcepts, "shares"),
            First(EquityConcepts, "USD"),
            DebtConcepts.ToDictionary(c => c, c => Values(gaap, c, "USD")));
        return financials.DilutedEps.Count == 0 && financials.Equity.Count == 0 ? null : financials;
    }

    // Only 10-K and 10-Q numbers (and their amendments), which are the audited and reviewed ones.
    private static IReadOnlyList<ReportedValue> Values(System.Text.Json.JsonElement gaap, string concept, string unit)
    {
        if (!gaap.TryGetProperty(concept, out var c) || !c.TryGetProperty("units", out var units) || !units.TryGetProperty(unit, out var rows))
            return [];

        static DateOnly Date(System.Text.Json.JsonElement e) => DateOnly.Parse(e.GetString()!, System.Globalization.CultureInfo.InvariantCulture);

        var values = new List<ReportedValue>();
        foreach (var row in rows.EnumerateArray())
        {
            var form = row.TryGetProperty("form", out var f) ? f.GetString() ?? "" : "";
            if (!form.StartsWith("10-K", StringComparison.Ordinal) && !form.StartsWith("10-Q", StringComparison.Ordinal)) continue;

            values.Add(new ReportedValue(
                row.TryGetProperty("start", out var start) ? Date(start) : null,
                Date(row.GetProperty("end")),
                row.GetProperty("val").GetDouble(),
                Date(row.GetProperty("filed"))));
        }
        return values;
    }

    // A row of the filing's index page whose type is EX-99.1, the press release. File names vary too much
    // to go by (ex991_q326financialresults.htm, exhibit9914.htm), but the index always labels the type.
    private static readonly System.Text.RegularExpressions.Regex PressReleaseRow = new(
        @"<tr[^>]*>(?:(?!</tr>).)*?href=""(?:/ix\?doc=)?(?<path>/Archives/[^""]+\.htm)""(?:(?!</tr>).)*?>\s*EX-99\.1\s*<",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Singleline);

    private async Task<EarningsRelease> ReleaseAsync(long cik, string accession, DateOnly filed, CancellationToken ct)
    {
        var index = $"https://www.sec.gov/Archives/edgar/data/{cik}/{accession.Replace("-", "")}/{accession}-index.htm";
        var fallback = new EarningsRelease(filed, index, null);

        var row = PressReleaseRow.Match(await GetTextAsync(index, ct));
        if (!row.Success) return fallback;

        var document = "https://www.sec.gov" + row.Groups["path"].Value;
        var html = await GetTextAsync(document, ct);
        return new EarningsRelease(filed, document, BuyWindow.Core.EarningsCallLink.Find(html));
    }

    private async Task<string> GetTextAsync(string url, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("User-Agent", $"BuyWindow personal watchlist {Contact}");
        using var response = await httpClients.CreateClient(HttpClientName).SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(ct);
    }

    private async Task<IReadOnlyDictionary<string, long>> CiksAsync(DateTimeOffset now, CancellationToken ct)
    {
        if (_ciks is { } cached && now - cached.At < TimeSpan.FromDays(1)) return cached.Ciks;

        var rows = await GetAsync<Dictionary<string, TickerRow>>("https://www.sec.gov/files/company_tickers.json", ct);
        var ciks = rows.Values
            .GroupBy(r => r.Ticker, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Cik, StringComparer.OrdinalIgnoreCase);
        _ciks = (now, ciks);
        return ciks;
    }

    private async Task<T> GetAsync<T>(string url, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("User-Agent", $"BuyWindow personal watchlist {Contact}");
        using var response = await httpClients.CreateClient(HttpClientName).SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(ct) ?? throw new System.Text.Json.JsonException("Empty response.");
    }

    private sealed record TickerRow(
        [property: JsonPropertyName("cik_str")] long Cik,
        [property: JsonPropertyName("ticker")] string Ticker);

    private sealed record Submissions([property: JsonPropertyName("filings")] Filings Filings);

    private sealed record Filings([property: JsonPropertyName("recent")] RecentFilings Recent);

    private sealed record RecentFilings(
        [property: JsonPropertyName("accessionNumber")] List<string> AccessionNumber,
        [property: JsonPropertyName("filingDate")] List<string> FilingDate,
        [property: JsonPropertyName("form")] List<string> Form,
        [property: JsonPropertyName("items")] List<string> Items);
}
