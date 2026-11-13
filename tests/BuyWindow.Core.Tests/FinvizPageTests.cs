namespace BuyWindow.Core.Tests;

public class FinvizPageTests
{
    private static readonly DateOnly Today = new(2026, 11, 13);

    private static FinvizSnapshot Mu() =>
        FinvizPage.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "finviz-mu.html")), Today)
        ?? throw new InvalidOperationException("The fixture didn't parse.");

    [Fact]
    public void Reads_every_reading_in_finvizs_column_order()
    {
        var snapshot = Mu();
        Assert.Equal(6, snapshot.Columns.Count);
        Assert.Equal(84, snapshot.Columns.Sum(c => c.Count));
        Assert.Equal("Index", snapshot.Columns[0][0].Label);
        Assert.Equal("NDX, S&P 500, RUI", snapshot.Columns[0][0].Value);
    }

    [Theory]
    [InlineData("P/E", "10.20")]
    [InlineData("Short Float", "2.07%")]
    [InlineData("Short Interest", "23.39M")]
    [InlineData("Short Ratio", "0.84")]
    [InlineData("Dividend Est.", "0.72 (0.10%)")] // a label inside a link
    public void Reads_readings_by_label(string label, string value) =>
        Assert.Equal(value, Mu().Field(label)?.Value);

    [Fact]
    public void Keeps_finvizs_colors()
    {
        Assert.Equal(FinvizTone.Positive, Mu().Field("P/E")?.Tone);
        Assert.Equal(FinvizTone.Neutral, Mu().Field("Price")?.Tone);
    }

    [Fact]
    public void Takes_the_latest_ten_headlines_and_carries_each_date_forward()
    {
        var news = Mu().Headlines;
        Assert.Equal(10, news.Count);
        Assert.Equal(new DateTime(2026, 11, 13, 7, 34, 0), news[0].At); // "Today 07:34AM"
        Assert.Equal("AI After Jensen: Stocks to Watch", news[0].Title);
        Assert.Equal("The Wall Street Journal", news[0].Source);
        Assert.StartsWith("https://www.wsj.com/", news[0].Url);
        Assert.Equal(new DateTime(2026, 11, 12, 17, 21, 0), news[2].At); // "Nov-12-26 05:21PM"
        Assert.Equal(new DateTime(2026, 11, 12, 16, 50, 0), news[3].At); // "04:50PM", the same day
    }

    [Fact]
    public void A_page_without_a_quote_table_is_null() =>
        Assert.Null(FinvizPage.Parse("<html><body>Access denied</body></html>", Today));

    [Fact]
    public void Links_to_the_quote_page() =>
        Assert.Equal("https://finviz.com/quote.ashx?t=BRK.B", FinvizSnapshot.QuoteUrl("brk.b"));
}
