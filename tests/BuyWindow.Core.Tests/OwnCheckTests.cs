namespace BuyWindow.Core.Tests;

public class OwnCheckTests
{
    private static FinvizSnapshot Finviz(params (string Label, string Value)[] fields) =>
        new([fields.Select(f => new FinvizField(f.Label, f.Value, FinvizTone.Neutral)).ToList()], []);

    private static LynchResult Lynch(double? growth = null, double? peg = null, double? debt = null) => new(
    [
        new("EPS growth", "", "", growth is null ? null : growth > 0.15, growth),
        new("PEG", "", "", peg is null ? null : peg < 2, peg),
        new("Debt/Equity", "", "", debt is null ? null : debt < 0.35, debt),
    ], null, null);

    private static OwnRule Rule(OwnResult r, string name) => r.Rules.Single(x => x.Name == name);

    [Fact]
    public void Passing_every_rule_means_own_it()
    {
        var result = OwnCheck.For(Finviz(("Forward P/E", "18.2"), ("PEG", "1.40"), ("Debt/Eq", "0.20"), ("Short Float", "2.10%")), Lynch(growth: 0.08));
        Assert.Equal(OwnVerdict.OwnIt, result.Verdict);
        Assert.Equal(5, result.Checked);
    }

    [Fact]
    public void Any_failure_means_review_and_names_it()
    {
        var result = OwnCheck.For(Finviz(("Forward P/E", "48.0"), ("Short Float", "12.50%")), Lynch(growth: 0.3));
        Assert.Equal(OwnVerdict.Review, result.Verdict);
        Assert.Equal(["Forward P/E", "Short float"], result.Failures.Select(f => f.Name));
    }

    [Fact]
    public void Missing_finviz_numbers_fall_back_to_the_sec()
    {
        var result = OwnCheck.For(Finviz(("PEG", "-"), ("Debt/Eq", "-")), Lynch(peg: 1.2, debt: 0.5));
        Assert.Equal("SEC", Rule(result, "PEG").Source);
        Assert.True(Rule(result, "PEG").Passes);
        Assert.False(Rule(result, "Debt/Equity").Passes);
    }

    [Fact]
    public void Sec_growth_wins_over_finviz_estimates()
    {
        var result = OwnCheck.For(Finviz(("EPS this Y", "25.00%")), Lynch(growth: -0.1));
        Assert.Equal("SEC", Rule(result, "EPS growth").Source);
        Assert.False(Rule(result, "EPS growth").Passes);
    }

    [Fact]
    public void Too_little_data_is_not_checked()
    {
        Assert.Equal(OwnVerdict.NotChecked, OwnCheck.For(null, null).Verdict);
        Assert.Equal(OwnVerdict.NotChecked, OwnCheck.For(Finviz(("Short Float", "1%")), null).Verdict);
    }

    [Fact]
    public void A_negative_forward_pe_fails()
    {
        Assert.False(Rule(OwnCheck.For(Finviz(("Forward P/E", "-12.0")), null), "Forward P/E").Passes);
    }

    [Theory]
    [InlineData("25.43", 25.43)]
    [InlineData("3.45%", 0.0345)]
    [InlineData("-", null)]
    [InlineData("", null)]
    public void Reads_finviz_numbers(string text, double? expected) => Assert.Equal(expected, OwnCheck.Number(text));
}
