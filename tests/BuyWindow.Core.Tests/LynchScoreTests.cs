namespace BuyWindow.Core.Tests;

public class LynchScoreTests
{
    private static DateOnly D(string s) => DateOnly.Parse(s, System.Globalization.CultureInfo.InvariantCulture);

    private static ReportedValue Period(string start, string end, double value, string filed) => new(D(start), D(end), value, D(filed));

    private static ReportedValue Instant(string end, double value) => new(null, D(end), value, D(end).AddDays(40));

    // A calendar-year company that has filed its 10-Q for June 2026.
    private static readonly IReadOnlyList<ReportedValue> Eps =
    [
        Period("2024-01-01", "2024-06-30", 1.8, "2024-08-01"),
        Period("2024-01-01", "2024-12-31", 4.0, "2025-02-01"),
        Period("2025-01-01", "2025-06-30", 2.2, "2025-08-01"),
        Period("2025-01-01", "2025-12-31", 5.0, "2026-02-01"),
        Period("2026-04-01", "2026-06-30", 1.5, "2026-08-01"),
        Period("2026-01-01", "2026-06-30", 2.8, "2026-08-01"),
    ];

    private static Financials Company(
        IReadOnlyList<ReportedValue>? eps = null, double equity = 1000, Dictionary<string, IReadOnlyList<ReportedValue>>? debt = null) => new(
        eps ?? Eps,
        [Period("2026-04-01", "2026-06-30", 100e6, "2026-08-01"), Period("2026-01-01", "2026-06-30", 90e6, "2026-08-01")],
        [Instant("2025-12-31", 900), Instant("2026-06-30", equity)],
        debt ?? new()
        {
            ["LongTermDebtNoncurrent"] = [Instant("2026-06-30", 200), Instant("2025-12-31", 900)],
            ["LongTermDebtCurrent"] = [Instant("2026-06-30", 50)],
            ["CommercialPaper"] = [Instant("2026-06-30", 20)],
        });

    private static LynchCheck Check(LynchResult result, string name) => result.Checks.Single(c => c.Name == name);

    [Fact]
    public void Trailing_earnings_add_last_year_to_the_year_so_far()
    {
        var now = LynchScore.Trailing(Eps)!.Value;
        Assert.Equal(5.6, now.Value, 6); // 5.0 + 2.8 - 2.2
        Assert.Equal(D("2026-06-30"), now.End);

        var yearAgo = LynchScore.Trailing(Eps, D("2025-06-30"))!.Value;
        Assert.Equal(4.4, yearAgo.Value, 6); // 4.0 + 2.2 - 1.8
    }

    [Fact]
    public void A_full_year_is_used_as_it_is() =>
        Assert.Equal(5.0, LynchScore.Trailing(Eps, D("2025-12-31"))!.Value.Value, 6);

    [Fact]
    public void A_newer_filing_of_the_same_period_wins()
    {
        var restated = Eps.Append(Period("2025-01-01", "2025-12-31", 6.0, "2027-02-01")).ToList();
        Assert.Equal(6.6, LynchScore.Trailing(restated)!.Value.Value, 6);
    }

    [Fact]
    public void Without_last_year_the_year_so_far_isnt_enough() =>
        Assert.Null(LynchScore.Trailing([Period("2026-01-01", "2026-06-30", 2.8, "2026-08-01")]));

    [Fact]
    public void Checks_each_rule()
    {
        var result = LynchScore.For(Company(), price: 100);

        Assert.Equal("17.9", Check(result, "Trailing P/E").Reading); // 100 / 5.6
        Assert.True(Check(result, "Trailing P/E").Passes);
        Assert.Equal("+27%", Check(result, "EPS growth").Reading); // 5.6 / 4.4
        Assert.True(Check(result, "EPS growth").Passes);
        Assert.Equal("0.65", Check(result, "PEG").Reading);
        Assert.True(Check(result, "PEG").Passes);
        Assert.Equal("27%", Check(result, "Debt/Equity").Reading); // (200 + 50 + 20) / 1000
        Assert.True(Check(result, "Debt/Equity").Passes);
        Assert.Equal("$10B", Check(result, "Market value").Reading); // the quarter's 100M shares
        Assert.True(Check(result, "Market value").Passes);
        Assert.Null(Check(result, "Forward P/E").Passes);

        Assert.Equal(5, result.Passed);
        Assert.Equal(5, result.Checked);
        Assert.Equal(D("2026-06-30"), result.EarningsThrough);
    }

    [Fact]
    public void An_expensive_price_fails_the_valuation_rules()
    {
        var result = LynchScore.For(Company(), price: 400); // P/E 71, PEG 2.6, $40B
        Assert.False(Check(result, "Trailing P/E").Passes);
        Assert.False(Check(result, "PEG").Passes);
        Assert.True(Check(result, "Market value").Passes);
        Assert.Equal(3, result.Passed);
    }

    [Fact]
    public void A_loss_fails_the_earnings_rules()
    {
        var losing = Eps.Append(Period("2026-01-01", "2026-06-30", -9, "2026-09-01")).ToList();
        var result = LynchScore.For(Company(losing), price: 100);
        Assert.Equal("Losing money", Check(result, "Trailing P/E").Reading);
        Assert.False(Check(result, "Trailing P/E").Passes);
        Assert.False(Check(result, "EPS growth").Passes);
        Assert.False(Check(result, "PEG").Passes);
    }

    [Fact]
    public void Long_term_debt_already_includes_the_current_part()
    {
        var result = LynchScore.For(Company(debt: new()
        {
            ["LongTermDebt"] = [Instant("2026-06-30", 400)],
            ["LongTermDebtCurrent"] = [Instant("2026-06-30", 50)],
            ["ShortTermBorrowings"] = [Instant("2026-06-30", 100)],
        }), price: 100);
        Assert.Equal("50%", Check(result, "Debt/Equity").Reading);
        Assert.False(Check(result, "Debt/Equity").Passes);
    }

    [Fact]
    public void Debt_with_leases_counts()
    {
        var result = LynchScore.For(Company(debt: new()
        {
            ["LongTermDebtAndCapitalLeaseObligationsIncludingCurrentMaturities"] = [Instant("2026-06-30", 1100)],
        }), price: 100);
        Assert.Equal("110%", Check(result, "Debt/Equity").Reading);
        Assert.False(Check(result, "Debt/Equity").Passes);
    }

    [Fact]
    public void No_debt_tags_isnt_taken_as_no_debt() =>
        Assert.Null(Check(LynchScore.For(Company(debt: new()), price: 100), "Debt/Equity").Passes);

    [Fact]
    public void Negative_equity_fails_debt()
    {
        var result = LynchScore.For(Company(equity: -50), price: 100);
        Assert.Equal("Equity below zero", Check(result, "Debt/Equity").Reading);
        Assert.False(Check(result, "Debt/Equity").Passes);
    }

    [Fact]
    public void Missing_filings_arent_counted()
    {
        var result = LynchScore.For(new Financials([], [], [], new Dictionary<string, IReadOnlyList<ReportedValue>>()), price: 100);
        Assert.Equal(0, result.Checked);
        Assert.All(result.Checks, c => Assert.Null(c.Passes));
    }
}

public class FinancialsAsOfTests
{
    [Fact]
    public void Leaves_out_numbers_filed_later()
    {
        var d = (string s) => DateOnly.Parse(s, System.Globalization.CultureInfo.InvariantCulture);
        var f = new Financials(
            [new(d("2025-01-01"), d("2025-12-31"), 5, d("2026-02-01")), new(d("2026-01-01"), d("2026-06-30"), 3, d("2026-08-01"))],
            [], [], new Dictionary<string, IReadOnlyList<ReportedValue>> { ["LongTermDebt"] = [new(null, d("2026-06-30"), 1, d("2026-08-01"))] });

        var then = f.AsOf(d("2026-03-01"));
        Assert.Single(then.DilutedEps);
        Assert.Empty(then.Debt["LongTermDebt"]);
    }
}
