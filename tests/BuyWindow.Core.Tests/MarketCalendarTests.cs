namespace BuyWindow.Core.Tests;

public class MarketCalendarTests
{
    [Theory]
    [InlineData("2026-01-01")] // New Year's Day
    [InlineData("2026-01-19")] // MLK Day
    [InlineData("2026-02-16")] // Presidents' Day
    [InlineData("2026-04-03")] // Good Friday
    [InlineData("2026-05-25")] // Memorial Day
    [InlineData("2026-06-19")] // Juneteenth
    [InlineData("2026-07-03")] // Independence Day, observed on Friday
    [InlineData("2026-09-07")] // Labor Day
    [InlineData("2026-11-26")] // Thanksgiving
    [InlineData("2026-12-25")] // Christmas
    [InlineData("2027-03-26")] // Good Friday
    [InlineData("2027-07-05")] // Independence Day, observed on Monday
    [InlineData("2026-10-31")] // Saturday
    [InlineData("2026-11-01")] // Sunday
    public void Market_is_closed(string date) =>
        Assert.False(MarketCalendar.IsTradingDay(DateOnly.Parse(date)));

    [Theory]
    [InlineData("2026-10-30")]
    [InlineData("2027-12-31")] // New Year's on a Saturday isn't observed the Friday before
    public void Market_is_open(string date) =>
        Assert.True(MarketCalendar.IsTradingDay(DateOnly.Parse(date)));

    [Fact]
    public void Lists_a_months_trading_days()
    {
        var days = MarketCalendar.TradingDays(2026, 11);
        Assert.Equal(new DateOnly(2026, 11, 2), days[0]);
        Assert.Equal(new DateOnly(2026, 11, 30), days[^1]);
        Assert.Equal(20, days.Count); // 21 weekdays less Thanksgiving
    }

    [Fact]
    public void Next_trading_day_skips_weekends_and_holidays()
    {
        Assert.Equal(new DateOnly(2026, 11, 2), MarketCalendar.NextTradingDay(new DateOnly(2026, 10, 30)));
        Assert.Equal(new DateOnly(2026, 11, 27), MarketCalendar.NextTradingDay(new DateOnly(2026, 11, 25)));
    }

    [Theory]
    [InlineData("2026-10-30 17:59", "2026-10-29 20:05")]
    [InlineData("2026-10-30 18:00", "2026-10-30 18:00")]
    [InlineData("2026-10-30 20:04", "2026-10-30 18:00")]
    [InlineData("2026-10-30 20:05", "2026-10-30 20:05")]
    [InlineData("2026-11-01 12:00", "2026-10-30 20:05")] // Sunday
    public void Refreshes_after_the_close_and_again_after_corrections(string now, string expected) =>
        Assert.Equal(DateTime.Parse(expected), MarketCalendar.LatestRefreshPoint(DateTime.Parse(now)));

    [Fact]
    public void Latest_expected_session_waits_until_corrections_finish_at_8pm_eastern()
    {
        var friday = new DateOnly(2026, 10, 30);
        Assert.Equal(new DateOnly(2026, 10, 29), MarketCalendar.LatestExpectedSession(friday.ToDateTime(new TimeOnly(19, 59))));
        Assert.Equal(friday, MarketCalendar.LatestExpectedSession(friday.ToDateTime(new TimeOnly(20, 0))));
        Assert.Equal(friday, MarketCalendar.LatestExpectedSession(new DateTime(2026, 11, 1, 12, 0, 0))); // Sunday
    }
}
