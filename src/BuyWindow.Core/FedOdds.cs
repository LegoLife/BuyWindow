namespace BuyWindow.Core;

/// <summary>One Kalshi "Fed decision" contract: its ticker suffix (H0, H25, C25, …) and its yes price in dollars.</summary>
public sealed record FedContract(string Outcome, double? Bid, double? Ask, double? Last);

/// <summary>The market's odds, 0-1, of a hike, a hold or a cut at one Fed meeting.</summary>
public sealed record FedMeeting(DateOnly Date, double Hike, double Hold, double Cut)
{
    public string Likeliest => Hike >= Hold && Hike >= Cut ? "hike" : Cut > Hold ? "cut" : "hold";
}

/// <summary>
/// Turns Kalshi prediction-market prices into hike/hold/cut odds. This is context shown beside the
/// buy score, never part of it: these markets have too little history to backtest.
/// </summary>
public static class FedOdds
{
    /// <summary>A hike this likely at the next meeting gets a warning on the dashboard.</summary>
    public const double HikeWarning = 0.30;

    public static FedMeeting? Meeting(DateOnly date, IEnumerable<FedContract> contracts)
    {
        double hike = 0, hold = 0, cut = 0;
        foreach (var c in contracts)
        {
            if (Price(c) is not { } p) continue;
            var o = c.Outcome.ToUpperInvariant();
            if (o == "H0") hold += p;
            else if (o.StartsWith('H')) hike += p;
            else if (o.StartsWith('C')) cut += p;
        }

        // Prices rarely sum to exactly $1, so scale them into odds that do.
        var total = hike + hold + cut;
        return total <= 0 ? null : new FedMeeting(date, hike / total, hold / total, cut / total);
    }

    // The middle of the bid and ask is fairer than the last trade, which can be stale on a quiet market.
    public static double? Price(FedContract c) =>
        c is { Bid: { } bid, Ask: { } ask } && ask > 0 && ask >= bid ? (bid + ask) / 2 : c.Last;
}
