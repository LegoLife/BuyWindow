using System.Globalization;

namespace BuyWindow.Core;

public enum OwnVerdict { OwnIt, Review, NotChecked }

/// <summary>One rule. <see cref="Passes"/> is null when neither Finviz nor the SEC filings have the number.</summary>
public sealed record OwnRule(string Name, string Needs, string Reading, bool? Passes, string Source);

public sealed record OwnResult(IReadOnlyList<OwnRule> Rules, OwnVerdict Verdict)
{
    public IEnumerable<OwnRule> Failures => Rules.Where(r => r.Passes == false);

    public int Checked => Rules.Count(r => r.Passes is not null);
}

/// <summary>
/// "Should I keep buying this at all?" Five fundamentals, read from Finviz first and the SEC filings when
/// Finviz has no number. Any failure means review it; passing needs at least <see cref="MinChecked"/> rules
/// answered. It changes quarterly at most, so it isn't part of the daily score and can't be backtested
/// with Finviz's numbers, which only exist for today.
/// </summary>
public static class OwnCheck
{
    public const double MaxForwardPe = 25;
    public const double MaxPeg = 2;
    public const double MaxDebtToEquity = 0.35;
    public const double MinEpsGrowth = 0;
    public const double MaxShortFloat = 0.10;
    public const int MinChecked = 3;

    public static OwnResult For(FinvizSnapshot? finviz, LynchResult? lynch)
    {
        double? Fv(string label) => finviz?.Field(label) is { } f ? Number(f.Value) : null;
        double? Sec(string name) => lynch?.Checks.FirstOrDefault(c => c.Name == name)?.Value;

        var rules = new List<OwnRule>
        {
            Rule("Forward P/E", $"Under {MaxForwardPe:0}", Fv("Forward P/E"), null, v => v > 0 && v < MaxForwardPe, v => $"{v:0.0}"),
            Rule("PEG", $"Under {MaxPeg:0}", Fv("PEG"), Sec("PEG"), v => v > 0 && v < MaxPeg, v => $"{v:0.00}"),
            Rule("Debt/Equity", $"Under {MaxDebtToEquity:0.00}", Fv("Debt/Eq"), Sec("Debt/Equity"), v => v < MaxDebtToEquity, v => $"{v:0.00}"),
            // The SEC's trailing 12 months come first: Finviz's "EPS this Y" mixes in estimates.
            Rule("EPS growth", "Above 0%", Sec("EPS growth"), Fv("EPS this Y"), v => v > MinEpsGrowth, v => $"{v * 100:+0;-0;0}%", secFirst: true),
            Rule("Short float", $"Under {MaxShortFloat:0%}", Fv("Short Float"), null, v => v < MaxShortFloat, v => $"{v * 100:0.0}%"),
        };

        var verdict = rules.Any(r => r.Passes == false) ? OwnVerdict.Review
            : rules.Count(r => r.Passes is not null) >= MinChecked ? OwnVerdict.OwnIt
            : OwnVerdict.NotChecked;
        return new OwnResult(rules, verdict);
    }

    private static OwnRule Rule(string name, string needs, double? first, double? second, Func<double, bool> passes, Func<double, string> format, bool secFirst = false)
    {
        var (value, source) = first is not null ? (first, secFirst ? "SEC" : "Finviz")
            : second is not null ? (second, secFirst ? "Finviz" : "SEC")
            : (null, "");
        return value is { } v
            ? new OwnRule(name, needs, format(v), passes(v), source)
            : new OwnRule(name, needs, "No data", null, source);
    }

    /// <summary>Finviz's readings as numbers: "25.43", "3.45%" (as 0.0345), "-" (null).</summary>
    public static double? Number(string text)
    {
        var t = text.Trim();
        var percent = t.EndsWith('%');
        if (percent) t = t[..^1];
        return double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? (percent ? v / 100 : v) : null;
    }
}
