using System.Net;
using System.Text.RegularExpressions;

namespace BuyWindow.Core;

/// <summary>
/// Finds where an earnings press release says the call's webcast is: the link written closest to
/// the word "webcast" (or "conference call"). Releases usually name the investor site, not the call itself.
/// </summary>
public static partial class EarningsCallLink
{
    private const int MaxDistance = 1500; // characters; further away, the link is about something else

    public static string? Find(string html)
    {
        var text = WebUtility.HtmlDecode(html);
        var mentions = Mention().Matches(text).Select(m => m.Index).ToList();
        if (mentions.Count == 0) return null;

        return Url().Matches(text)
            .Select(m => (Url: Normalize(m.Groups["url"].Value), Score: Score(m.Index, mentions)))
            .Where(c => c.Score is not null && !c.Url.Contains("sec.gov", StringComparison.OrdinalIgnoreCase))
            .OrderBy(c => c.Score)
            .Select(c => c.Url)
            .FirstOrDefault();
    }

    // Releases name the call first and then say where it is, so an address after a mention beats one before it.
    private static int? Score(int at, IReadOnlyList<int> mentions)
    {
        var after = mentions.Where(i => i <= at).Select(i => at - i).DefaultIfEmpty(int.MaxValue).Min();
        if (after <= MaxDistance) return after;

        var before = mentions.Where(i => i > at).Select(i => i - at).DefaultIfEmpty(int.MaxValue).Min();
        return before <= MaxDistance ? MaxDistance + before : null;
    }

    private static string Normalize(string url)
    {
        url = url.TrimEnd('.', ',', ';', ')', ']');
        return url.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? url : "https://" + url;
    }

    [GeneratedRegex(@"webcast|conference call|earnings call", RegexOptions.IgnoreCase)]
    private static partial Regex Mention();

    // A link's target, or an address written out in the text: with a scheme, with www., or a bare
    // name like investors.micron.com. The bare form skips email addresses and file names.
    [GeneratedRegex(@"href\s*=\s*[""'](?<url>https?://[^""']+)[""']|(?<url>(?:https?://|www\.)[^\s<>""']+)|(?<![@\w./-])(?<url>(?:[a-z0-9-]+\.)+(?:com|net|org|io|ai|co)(?:/[^\s<>""']*)?)(?![\w@-])", RegexOptions.IgnoreCase)]
    private static partial Regex Url();
}
