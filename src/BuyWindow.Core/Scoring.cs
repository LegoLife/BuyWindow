namespace BuyWindow.Core;

/// <summary>One day's 0-100 buy score and what each indicator contributed.</summary>
public sealed record DayScore(
    DateOnly Date,
    double RsiPoints,
    double BollingerPoints,
    double MacdPoints,
    double VolumePoints,
    double MarketPoints,
    double Rsi,
    double PercentB,
    double MacdHistogram,
    double VolumeRatio,
    bool MarketAboveAverage)
{
    public double Total => RsiPoints + BollingerPoints + MacdPoints + VolumePoints + MarketPoints;
}

/// <summary>
/// The fixed scoring rules. Every ticker uses the same numbers on purpose: tuning them per ticker
/// would fit past noise. Each of the five indicators is worth up to <see cref="MaxPoints"/>.
/// </summary>
public static class Scoring
{
    public const double MaxPoints = 20;

    /// <summary>Bars a ticker needs before its first score (MACD histogram plus two days of its trend).</summary>
    public const int WarmupBars = 36;

    public const int MarketAveragePeriod = 200;

    public static double RsiPoints(double rsi) => Scale(rsi, full: 30, none: 60);

    public static double BollingerPoints(double percentB) => Scale(percentB, full: 0, none: 0.5);

    public static double MacdPoints(double today, double yesterday, double dayBefore)
    {
        if (today >= 0) return yesterday < 0 ? MaxPoints : 0;
        if (today <= yesterday) return 0;
        return yesterday > dayBefore ? MaxPoints : MaxPoints / 2;
    }

    public static double VolumePoints(double ratio, bool downDay) =>
        downDay ? Scale(ratio, full: 2, none: 1) : 0;

    public static double MarketPoints(double close, double average) => close >= average ? MaxPoints : 0;

    /// <summary>Scores for every day with enough ticker and market history, oldest first.</summary>
    public static IReadOnlyList<DayScore> ScoreAll(IReadOnlyList<PriceBar> bars, IReadOnlyList<PriceBar> market)
    {
        var closes = bars.Select(b => b.Close).ToArray();
        var rsi = Indicators.Rsi(closes);
        var percentB = Indicators.PercentB(closes);
        var macd = Indicators.MacdHistogram(closes);
        var volume = Indicators.VolumeRatio(bars.Select(b => b.Volume).ToArray());

        var marketCloses = market.Select(b => b.Close).ToArray();
        var marketAverage = Indicators.Sma(marketCloses, MarketAveragePeriod);

        var scores = new List<DayScore>();
        var m = -1;
        for (var i = WarmupBars - 1; i < bars.Count; i++)
        {
            while (m + 1 < market.Count && market[m + 1].Date <= bars[i].Date) m++;

            if (m < 0 || marketAverage[m] is not { } average
                || rsi[i] is not { } r || percentB[i] is not { } b
                || macd[i] is not { } h || macd[i - 1] is not { } h1 || macd[i - 2] is not { } h2
                || volume[i] is not { } v)
                continue;

            scores.Add(new DayScore(
                bars[i].Date,
                RsiPoints(r),
                BollingerPoints(b),
                MacdPoints(h, h1, h2),
                VolumePoints(v, closes[i] < closes[i - 1]),
                MarketPoints(marketCloses[m], average),
                r, b, h, v, marketCloses[m] >= average));
        }
        return scores;
    }

    private static double Scale(double value, double full, double none)
    {
        var fraction = (value - none) / (full - none);
        return MaxPoints * Math.Clamp(fraction, 0, 1);
    }
}
