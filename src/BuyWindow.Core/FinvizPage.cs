using System.Globalization;
using AngleSharp.Html.Parser;

namespace BuyWindow.Core;

/// <summary>Whether Finviz colors a reading as good or bad.</summary>
public enum FinvizTone { Neutral, Positive, Negative }

/// <summary>One label and reading from Finviz's quote table, such as P/E or Short Float.</summary>
public sealed record FinvizField(string Label, string Value, FinvizTone Tone);

/// <summary>A news headline. <see cref="At"/> is US Eastern time, as Finviz shows it.</summary>
public sealed record FinvizHeadline(DateTime At, string Title, string Url, string Source);

/// <summary>
/// The quote table and headlines from a Finviz quote page. <see cref="Columns"/> keeps Finviz's own
/// grouping, so related readings stay together.
/// </summary>
public sealed record FinvizSnapshot(IReadOnlyList<IReadOnlyList<FinvizField>> Columns, IReadOnlyList<FinvizHeadline> Headlines)
{
    public static string QuoteUrl(string symbol) => $"https://finviz.com/quote.ashx?t={Uri.EscapeDataString(symbol.ToUpperInvariant())}";

    public FinvizField? Field(string label) =>
        Columns.SelectMany(c => c).FirstOrDefault(f => f.Label.Equals(label, StringComparison.OrdinalIgnoreCase));
}

public static class FinvizPage
{
    /// <summary>
    /// Reads a Finviz quote page. <paramref name="today"/> is today's date in US Eastern time, for
    /// headlines dated "Today". Null when the page has no quote table, such as an unknown ticker or a
    /// block page.
    /// </summary>
    public static FinvizSnapshot? Parse(string html, DateOnly today, int headlines = 10)
    {
        var document = new HtmlParser().ParseDocument(html);

        var columns = document.QuerySelectorAll("table.snapshot-table2")
            .Select(table => (IReadOnlyList<FinvizField>)table.QuerySelectorAll("tr")
                .Select(row => row.QuerySelectorAll("td"))
                .Where(cells => cells.Length >= 2)
                .Select(cells => new FinvizField(Clean(cells[0].TextContent), Clean(cells[1].TextContent), ToneOf(cells[1])))
                .Where(f => f.Label.Length > 0)
                .ToList())
            .Where(c => c.Count > 0)
            .ToList();
        if (columns.Count == 0) return null;

        var news = new List<FinvizHeadline>();
        var day = today;
        foreach (var row in document.QuerySelectorAll("table.news-table tr"))
        {
            var link = row.QuerySelector("a.tab-link-news");
            var cells = row.QuerySelectorAll("td");
            if (link is null || cells.Length < 2) continue; // ads and spacer rows

            // Finviz dates only the first headline of each day: "Nov-12-26 05:21PM", "Today 07:34AM", then "04:50PM".
            var stamp = Clean(cells[0].TextContent).Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (stamp.Length == 0) continue;
            if (stamp.Length > 1)
            {
                if (stamp[0].Equals("Today", StringComparison.OrdinalIgnoreCase)) day = today;
                else if (DateOnly.TryParseExact(stamp[0], "MMM-dd-yy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dated)) day = dated;
            }
            var time = TimeOnly.TryParseExact(stamp[^1], "hh:mmtt", CultureInfo.InvariantCulture, DateTimeStyles.None, out var t) ? t : TimeOnly.MinValue;

            var source = Clean(row.QuerySelector(".news-link-right span")?.TextContent ?? "").Trim('(', ')');
            news.Add(new FinvizHeadline(day.ToDateTime(time), Clean(link.TextContent), link.GetAttribute("href") ?? "", source));
            if (news.Count == headlines) break;
        }

        return new FinvizSnapshot(columns, news);
    }

    private static FinvizTone ToneOf(AngleSharp.Dom.IElement cell)
    {
        // A reading with two numbers can mix colors; the first one sets the tone.
        var colored = cell.QuerySelector(".is-positive, .is-negative");
        return colored?.ClassList.Contains("is-positive") == true ? FinvizTone.Positive
            : colored is not null ? FinvizTone.Negative
            : FinvizTone.Neutral;
    }

    private static string Clean(string text) => string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
