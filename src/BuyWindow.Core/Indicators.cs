namespace BuyWindow.Core;

/// <summary>Standard technical indicators. Each result lines up with its input; null means still warming up.</summary>
public static class Indicators
{
    public static double?[] Rsi(IReadOnlyList<double> closes, int period = 14)
    {
        var result = new double?[closes.Count];
        if (closes.Count <= period) return result;

        double gain = 0, loss = 0;
        for (var i = 1; i <= period; i++)
        {
            var change = closes[i] - closes[i - 1];
            gain += Math.Max(change, 0);
            loss += Math.Max(-change, 0);
        }
        gain /= period;
        loss /= period;
        result[period] = RsiFrom(gain, loss);

        for (var i = period + 1; i < closes.Count; i++)
        {
            var change = closes[i] - closes[i - 1];
            gain = (gain * (period - 1) + Math.Max(change, 0)) / period;
            loss = (loss * (period - 1) + Math.Max(-change, 0)) / period;
            result[i] = RsiFrom(gain, loss);
        }
        return result;
    }

    private static double RsiFrom(double gain, double loss)
    {
        if (gain == 0 && loss == 0) return 50;
        return loss == 0 ? 100 : 100 - 100 / (1 + gain / loss);
    }

    /// <summary>Where the close sits in its Bollinger Bands: 0 is the lower band, 1 the upper.</summary>
    public static double?[] PercentB(IReadOnlyList<double> closes, int period = 20, double widths = 2)
    {
        var result = new double?[closes.Count];
        for (var i = period - 1; i < closes.Count; i++)
        {
            var window = closes.Skip(i - period + 1).Take(period).ToArray();
            var mean = window.Average();
            var deviation = Math.Sqrt(window.Sum(c => (c - mean) * (c - mean)) / period);
            result[i] = deviation == 0 ? 0.5 : (closes[i] - (mean - widths * deviation)) / (2 * widths * deviation);
        }
        return result;
    }

    public static double?[] MacdHistogram(IReadOnlyList<double> closes, int fast = 12, int slow = 26, int signal = 9)
    {
        var result = new double?[closes.Count];
        var fastEma = Ema(closes, fast);
        var slowEma = Ema(closes, slow);
        if (closes.Count < slow + signal - 1) return result;

        var line = new double[closes.Count - slow + 1];
        for (var i = 0; i < line.Length; i++)
            line[i] = fastEma[i + slow - 1]!.Value - slowEma[i + slow - 1]!.Value;

        var signalEma = Ema(line, signal);
        for (var i = signal - 1; i < line.Length; i++)
            result[i + slow - 1] = line[i] - signalEma[i]!.Value;
        return result;
    }

    /// <summary>Today's volume as a multiple of the average over the preceding days.</summary>
    public static double?[] VolumeRatio(IReadOnlyList<double> volumes, int period = 20)
    {
        var result = new double?[volumes.Count];
        for (var i = period; i < volumes.Count; i++)
        {
            var average = volumes.Skip(i - period).Take(period).Average();
            result[i] = average == 0 ? null : volumes[i] / average;
        }
        return result;
    }

    public static double?[] Sma(IReadOnlyList<double> values, int period)
    {
        var result = new double?[values.Count];
        var sum = 0.0;
        for (var i = 0; i < values.Count; i++)
        {
            sum += values[i];
            if (i >= period) sum -= values[i - period];
            if (i >= period - 1) result[i] = sum / period;
        }
        return result;
    }

    private static double?[] Ema(IReadOnlyList<double> values, int period)
    {
        var result = new double?[values.Count];
        if (values.Count < period) return result;

        var multiplier = 2.0 / (period + 1);
        var ema = values.Take(period).Average();
        result[period - 1] = ema;
        for (var i = period; i < values.Count; i++)
        {
            ema += (values[i] - ema) * multiplier;
            result[i] = ema;
        }
        return result;
    }
}
