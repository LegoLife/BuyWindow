namespace BuyWindow.Core;

/// <summary>
/// The regular US equity market schedule. One-off closures aren't predictable, so past months
/// should be read from actual price bars; this is for the current and future months.
/// </summary>
public static class MarketCalendar
{
    // Tiingo publishes most prices by 5:30 PM Eastern and takes corrections until 8 PM.
    private static readonly TimeOnly CorrectionsFinished = new(20, 0);
    private static readonly TimeOnly FirstRefresh = new(18, 0);
    private static readonly TimeOnly CorrectionsRefresh = new(20, 5);

    public static bool IsTradingDay(DateOnly date) =>
        date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday) && !IsHoliday(date);

    public static IReadOnlyList<DateOnly> TradingDays(int year, int month) =>
        Enumerable.Range(1, DateTime.DaysInMonth(year, month))
            .Select(day => new DateOnly(year, month, day))
            .Where(IsTradingDay)
            .ToList();

    public static DateOnly NextTradingDay(DateOnly date)
    {
        do date = date.AddDays(1);
        while (!IsTradingDay(date));
        return date;
    }

    public static DateOnly PreviousTradingDay(DateOnly date)
    {
        do date = date.AddDays(-1);
        while (!IsTradingDay(date));
        return date;
    }

    /// <summary>The most recent session whose closing data should be final, given Eastern wall-clock time.</summary>
    public static DateOnly LatestExpectedSession(DateTime easternNow)
    {
        var today = DateOnly.FromDateTime(easternNow);
        return IsTradingDay(today) && TimeOnly.FromDateTime(easternNow) >= CorrectionsFinished
            ? today
            : PreviousTradingDay(today);
    }

    /// <summary>The latest scheduled price refresh at or before the given Eastern wall-clock time.</summary>
    public static DateTime LatestRefreshPoint(DateTime easternNow)
    {
        var today = DateOnly.FromDateTime(easternNow);
        if (IsTradingDay(today))
        {
            var time = TimeOnly.FromDateTime(easternNow);
            if (time >= CorrectionsRefresh) return today.ToDateTime(CorrectionsRefresh);
            if (time >= FirstRefresh) return today.ToDateTime(FirstRefresh);
        }
        return PreviousTradingDay(today).ToDateTime(CorrectionsRefresh);
    }

    private static bool IsHoliday(DateOnly date)
    {
        var year = date.Year;
        return date == Observed(new DateOnly(year, 1, 1), observeOnFriday: false)
            || date == NthWeekday(year, 1, DayOfWeek.Monday, 3)
            || date == NthWeekday(year, 2, DayOfWeek.Monday, 3)
            || date == EasterSunday(year).AddDays(-2)
            || date == LastWeekday(year, 5, DayOfWeek.Monday)
            || (year >= 2022 && date == Observed(new DateOnly(year, 6, 19)))
            || date == Observed(new DateOnly(year, 7, 4))
            || date == NthWeekday(year, 9, DayOfWeek.Monday, 1)
            || date == NthWeekday(year, 11, DayOfWeek.Thursday, 4)
            || date == Observed(new DateOnly(year, 12, 25));
    }

    private static DateOnly Observed(DateOnly holiday, bool observeOnFriday = true) => holiday.DayOfWeek switch
    {
        DayOfWeek.Saturday when observeOnFriday => holiday.AddDays(-1),
        DayOfWeek.Sunday => holiday.AddDays(1),
        _ => holiday,
    };

    private static DateOnly NthWeekday(int year, int month, DayOfWeek weekday, int n)
    {
        var first = new DateOnly(year, month, 1);
        var offset = ((int)weekday - (int)first.DayOfWeek + 7) % 7;
        return first.AddDays(offset + (n - 1) * 7);
    }

    private static DateOnly LastWeekday(int year, int month, DayOfWeek weekday)
    {
        var last = new DateOnly(year, month, DateTime.DaysInMonth(year, month));
        var offset = ((int)last.DayOfWeek - (int)weekday + 7) % 7;
        return last.AddDays(-offset);
    }

    // Anonymous Gregorian algorithm.
    private static DateOnly EasterSunday(int year)
    {
        var a = year % 19;
        var b = year / 100;
        var c = year % 100;
        var d = b / 4;
        var e = b % 4;
        var f = (b + 8) / 25;
        var g = (b - f + 1) / 3;
        var h = (19 * a + b - d - g + 15) % 30;
        var i = c / 4;
        var k = c % 4;
        var l = (32 + 2 * e + 2 * i - h - k) % 7;
        var m = (a + 11 * h + 22 * l) / 451;
        var month = (h + l - 7 * m + 114) / 31;
        var day = (h + l - 7 * m + 114) % 31 + 1;
        return new DateOnly(year, month, day);
    }
}
