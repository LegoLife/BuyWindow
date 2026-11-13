namespace BuyWindow.Core.Tests;

public class EarningsCallLinkTests
{
    [Fact]
    public void Finds_the_address_given_for_the_webcast()
    {
        const string html = """
            <p>Visit our website at <a href="https://www.example.com">example.com</a>.</p>
            <p>Revenue grew.</p>
            <p>The live webcast will be available under the Investor Relations section of our website at
            https://investor.bloomenergy.com.</p>
            """;

        Assert.Equal("https://investor.bloomenergy.com", EarningsCallLink.Find(html));
    }

    [Fact]
    public void Prefers_the_link_nearest_the_call_over_others()
    {
        const string html = """
            <p>See <a href="https://ir.tesla.com/">ir.tesla.com</a> for the shareholder deck.</p>
            <p>Tesla will host a conference call; a live webcast is at
            <a href="https://events.q4inc.com/attendee/123">this link</a>.</p>
            """;

        Assert.Equal("https://events.q4inc.com/attendee/123", EarningsCallLink.Find(html));
    }

    [Fact]
    public void Adds_https_to_a_bare_address_and_drops_trailing_punctuation()
    {
        const string html = "<p>The webcast can be accessed at www.micron.com/investors.</p>";
        Assert.Equal("https://www.micron.com/investors", EarningsCallLink.Find(html));
    }

    [Fact]
    public void Finds_a_bare_address_without_www()
    {
        const string html = "<p>A live webcast of the call will be available online at investors.micron.com.</p>";
        Assert.Equal("https://investors.micron.com", EarningsCallLink.Find(html));
    }

    [Fact]
    public void Prefers_the_address_in_the_webcast_sentence_over_a_nearby_homepage_link()
    {
        const string html = """
            <p>About <a href="https://www.sandisk.com/">Sandisk</a>.</p>
            <p>The investment community conference call will be broadcast live online today at 1:30 p.m. Pacific.
            The live and archived conference call/webcast and the earnings presentation can be accessed online at investor.sandisk.com.</p>
            """;

        Assert.Equal("https://investor.sandisk.com", EarningsCallLink.Find(html));
    }

    [Fact]
    public void A_bare_address_skips_email_addresses()
    {
        const string html = "<p>For the webcast, contact ir@micron.com.</p>";
        Assert.Null(EarningsCallLink.Find(html));
    }

    [Fact]
    public void Ignores_links_back_to_the_sec()
    {
        const string html = "<p>The webcast details are in <a href=\"https://www.sec.gov/x\">our filing</a>.</p>";
        Assert.Null(EarningsCallLink.Find(html));
    }

    [Fact]
    public void Returns_nothing_without_any_mention_of_the_call()
    {
        Assert.Null(EarningsCallLink.Find("<p>Visit https://www.example.com for more.</p>"));
    }
}
