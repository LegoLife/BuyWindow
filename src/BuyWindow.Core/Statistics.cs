namespace BuyWindow.Core;

public sealed record ConfidenceInterval(double Mean, double Low, double High, int Count);

public static class Statistics
{
    /// <summary>A two-sided t interval for the mean. With fewer than two values, the range is unbounded.</summary>
    public static ConfidenceInterval MeanConfidenceInterval(IReadOnlyList<double> values, double confidence = 0.95)
    {
        if (values.Count == 0) return new ConfidenceInterval(0, double.NegativeInfinity, double.PositiveInfinity, 0);

        var mean = values.Average();
        if (values.Count < 2) return new ConfidenceInterval(mean, double.NegativeInfinity, double.PositiveInfinity, 1);

        var variance = values.Sum(v => (v - mean) * (v - mean)) / (values.Count - 1);
        var margin = TCritical(confidence, values.Count - 1) * Math.Sqrt(variance / values.Count);
        return new ConfidenceInterval(mean, mean - margin, mean + margin, values.Count);
    }

    // Cornish-Fisher expansion of Student's t around the normal quantile.
    private static double TCritical(double confidence, int df)
    {
        var z = NormalQuantile(1 - (1 - confidence) / 2);
        double z2 = z * z, z3 = z2 * z, z5 = z3 * z2, z7 = z5 * z2, z9 = z7 * z2;
        var g1 = (z3 + z) / 4;
        var g2 = (5 * z5 + 16 * z3 + 3 * z) / 96;
        var g3 = (3 * z7 + 19 * z5 + 17 * z3 - 15 * z) / 384;
        var g4 = (79 * z9 + 776 * z7 + 1482 * z5 - 1920 * z3 - 945 * z) / 92160;
        double v = df;
        return z + g1 / v + g2 / (v * v) + g3 / (v * v * v) + g4 / (v * v * v * v);
    }

    // Acklam's rational approximation, for p in the upper tail.
    private static double NormalQuantile(double p)
    {
        double[] a = [-3.969683028665376e+01, 2.209460984245205e+02, -2.759285104469687e+02, 1.383577518672690e+02, -3.066479806614716e+01, 2.506628277459239e+00];
        double[] b = [-5.447609879822406e+01, 1.615858368580409e+02, -1.556989798598866e+02, 6.680131188771972e+01, -1.328068155288572e+01];
        double[] c = [-7.784894002430293e-03, -3.223964580411365e-01, -2.400758277161838e+00, -2.549732539343734e+00, 4.374664141464968e+00, 2.938163982698783e+00];
        double[] d = [7.784695709041462e-03, 3.224671290700398e-01, 2.445134137142996e+00, 3.754408661907416e+00];

        if (p <= 0.97575)
        {
            var q = p - 0.5;
            var r = q * q;
            return (((((a[0] * r + a[1]) * r + a[2]) * r + a[3]) * r + a[4]) * r + a[5]) * q
                / (((((b[0] * r + b[1]) * r + b[2]) * r + b[3]) * r + b[4]) * r + 1);
        }

        var t = Math.Sqrt(-2 * Math.Log(1 - p));
        return -(((((c[0] * t + c[1]) * t + c[2]) * t + c[3]) * t + c[4]) * t + c[5])
            / ((((d[0] * t + d[1]) * t + d[2]) * t + d[3]) * t + 1);
    }
}
