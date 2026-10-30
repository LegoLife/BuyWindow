namespace BuyWindow.Core;

/// <summary>One trading day of split- and dividend-adjusted prices.</summary>
public sealed record PriceBar(DateOnly Date, double Open, double High, double Low, double Close, double Volume);
