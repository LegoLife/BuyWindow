namespace BuyWindow.Core;

/// <summary>
/// A number from a 10-K or 10-Q. Amounts over a period (earnings, share counts) have a <see cref="Start"/>;
/// balance sheet amounts don't. The same number is often filed again a year later as a comparison, so
/// <see cref="Filed"/> picks the newest version.
/// </summary>
public sealed record ReportedValue(DateOnly? Start, DateOnly End, double Value, DateOnly Filed);

/// <summary>The parts of a company's filings the Lynch checks need. Debt is keyed by its us-gaap concept name.</summary>
public sealed record Financials(
    IReadOnlyList<ReportedValue> DilutedEps,
    IReadOnlyList<ReportedValue> Shares,
    IReadOnlyList<ReportedValue> Equity,
    IReadOnlyDictionary<string, IReadOnlyList<ReportedValue>> Debt);

/// <summary>One rule. <see cref="Passes"/> is null when the filings don't have what it needs.</summary>
public sealed record LynchCheck(string Name, string Rule, string Reading, bool? Passes);

public sealed record LynchResult(IReadOnlyList<LynchCheck> Checks, DateOnly? EarningsThrough, DateOnly? BalanceSheetAt)
{
    public int Passed => Checks.Count(c => c.Passes == true);

    public int Checked => Checks.Count(c => c.Passes is not null);
}

/// <summary>
/// Peter Lynch's six stock-picking rules, checked against SEC filings: trailing P/E under 25, forward P/E
/// under 15, debt under 35% of equity, earnings per share growing over 15%, PEG under 2 and a market value
/// over $5 billion. Forward P/E needs analysts' estimates, which the filings don't have, so it's never checked.
/// </summary>
public static class LynchScore
{
    public const double MaxTrailingPe = 25;
    public const double MaxForwardPe = 15;
    public const double MaxDebtToEquity = 0.35;
    public const double MinEpsGrowth = 0.15;
    public const double MaxPeg = 2;
    public const double MinMarketCap = 5e9;

    public static LynchResult For(Financials financials, double price)
    {
        var eps = Trailing(financials.DilutedEps);
        var priorEps = eps is { } now ? Trailing(financials.DilutedEps, now.End.AddYears(-1)) : null;
        double? growth = eps is { } e && priorEps is { Value: > 0 } p ? e.Value / p.Value - 1 : null;
        double? pe = eps is { Value: > 0 } positive ? price / positive.Value : null;

        var checks = new List<LynchCheck>
        {
            new("Trailing P/E", $"Under {MaxTrailingPe:0}",
                eps is null ? "No earnings filed" : pe is { } v ? $"{v:0.0}" : "Losing money",
                eps is null ? null : pe < MaxTrailingPe),
            new("Forward P/E", $"Under {MaxForwardPe:0}", "Needs analysts' estimates, which the SEC doesn't have", null),
            DebtCheck(financials),
            new("EPS growth", $"Over {MinEpsGrowth:0%}",
                priorEps is null ? "Needs two years of earnings" : growth is { } g ? Percent(g) : "No profit a year earlier",
                priorEps is null ? null : growth > MinEpsGrowth),
            PegCheck(eps is not null && priorEps is not null, pe, growth),
            MarketCapCheck(financials, price),
        };

        return new LynchResult(checks, eps?.End, Latest(financials.Equity)?.End);
    }

    /// <summary>
    /// Earnings per share for the year ending at the newest period filed, or the one closest to
    /// <paramref name="near"/>. A 10-Q only covers the year so far, so this adds the last full year
    /// and takes off the same months of that year: 6 months this year + last year - 6 months last year.
    /// </summary>
    public static (double Value, DateOnly End)? Trailing(IReadOnlyList<ReportedValue> facts, DateOnly? near = null)
    {
        var periods = facts
            .Where(f => f.Start is not null)
            .GroupBy(f => (f.Start, f.End))
            .Select(g => g.MaxBy(f => f.Filed)!)
            .ToList();

        var ends = periods.Select(p => p.End).Distinct();
        DateOnly? end = near is { } target
            ? ends.Where(e => Math.Abs(e.DayNumber - target.DayNumber) <= 10).OrderBy(e => Math.Abs(e.DayNumber - target.DayNumber)).Cast<DateOnly?>().FirstOrDefault()
            : ends.Cast<DateOnly?>().Max();
        if (end is not { } at) return null;

        var endingThere = periods.Where(p => p.End == at).ToList();
        if (endingThere.FirstOrDefault(IsYear) is { } year) return (year.Value, at);

        // The longest period ending there that's shorter than a year is the year so far.
        var soFar = endingThere.Where(p => Days(p) < 350).MaxBy(Days);
        if (soFar is null) return null;

        var lastYear = periods.FirstOrDefault(p => IsYear(p) && Near(p.End, soFar.Start!.Value.AddDays(-1), 7));
        var soFarLastYear = periods.FirstOrDefault(p =>
            Near(p.End, at.AddYears(-1), 7) && Math.Abs(Days(p) - Days(soFar)) <= 10);
        if (lastYear is null || soFarLastYear is null) return null;

        return (lastYear.Value + soFar.Value - soFarLastYear.Value, at);
    }

    private static LynchCheck DebtCheck(Financials financials)
    {
        const string name = "Debt/Equity", rule = "Under 35%";
        if (Latest(financials.Equity) is not { } equity) return new(name, rule, "No balance sheet filed", null);

        double? At(string concept) => financials.Debt.TryGetValue(concept, out var facts)
            ? facts.Where(f => f.Start is null && f.End == equity.End).MaxBy(f => f.Filed)?.Value
            : null;

        double? First(params string[] concepts) => concepts.Select(At).FirstOrDefault(v => v is not null);

        // Companies tag debt with or without leases, and as one total or split by when it's due.
        var total = First("LongTermDebt", "LongTermDebtAndCapitalLeaseObligationsIncludingCurrentMaturities");
        var noncurrent = First("LongTermDebtNoncurrent", "LongTermDebtAndCapitalLeaseObligations");
        var current = First("LongTermDebtCurrent", "LongTermDebtAndCapitalLeaseObligationsCurrent");
        var debtCurrent = At("DebtCurrent"); // the part due this year plus short-term borrowings
        var shortTerm = First("ShortTermBorrowings", "CommercialPaper", "NotesAndLoansPayableCurrent");

        double? debt = total is not null || current is not null ? (total ?? (noncurrent ?? 0) + current!.Value) + (shortTerm ?? 0)
            : debtCurrent is not null ? (noncurrent ?? 0) + debtCurrent.Value
            : noncurrent is not null || shortTerm is not null ? (noncurrent ?? 0) + (shortTerm ?? 0)
            : null;

        if (equity.Value <= 0) return new(name, rule, "Equity below zero", false);

        if (debt is null) return new(name, rule, "No debt found in the filings", null);

        var ratio = debt.Value / equity.Value;
        return new(name, rule, $"{ratio:0%}", ratio < MaxDebtToEquity);
    }

    // PEG is the P/E over the growth rate in percent.
    private static LynchCheck PegCheck(bool hasEarnings, double? pe, double? growth)
    {
        var rule = $"Under {MaxPeg:0}";
        if (!hasEarnings) return new("PEG", rule, "Needs two years of earnings", null);
        if (pe is not { } p || growth is not > 0) return new("PEG", rule, "Needs a profit and growth", false);

        var peg = p / (growth.Value * 100);
        return new("PEG", rule, $"{peg:0.00}", peg < MaxPeg);
    }

    private static LynchCheck MarketCapCheck(Financials financials, double price)
    {
        const string name = "Market value";
        var rule = $"Over {Money(MinMarketCap)}";

        // The weighted average from the newest period counts every share class.
        var shares = financials.Shares
            .Where(f => f.Value > 0)
            .OrderByDescending(f => f.End)
            .ThenBy(f => f.Start is null ? 0 : Days(f))
            .ThenByDescending(f => f.Filed)
            .FirstOrDefault();
        if (shares is null) return new(name, rule, "No share count filed", null);

        var value = shares.Value * price;
        return new(name, rule, Money(value), value > MinMarketCap);
    }

    private static ReportedValue? Latest(IReadOnlyList<ReportedValue> facts) =>
        facts.Where(f => f.Start is null).OrderByDescending(f => f.End).ThenByDescending(f => f.Filed).FirstOrDefault();

    private static int Days(ReportedValue p) => p.End.DayNumber - p.Start!.Value.DayNumber;

    private static bool IsYear(ReportedValue p) => Days(p) is >= 350 and <= 380;

    private static bool Near(DateOnly a, DateOnly b, int days) => Math.Abs(a.DayNumber - b.DayNumber) <= days;

    private static string Percent(double value) => $"{value * 100:+0;-0;0}%";

    public static string Money(double value) => value switch
    {
        >= 1e12 => $"${value / 1e12:0.#}T",
        >= 1e9 => $"${value / 1e9:0.#}B",
        _ => $"${value / 1e6:0}M",
    };
}
