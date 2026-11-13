namespace BuyWindow.Core;

/// <summary>A price against the close before it.</summary>
public sealed record PriceMove(double Price, double PreviousClose)
{
    public double Change => Price - PreviousClose;

    public double ChangePercent => PreviousClose == 0 ? 0 : Change / PreviousClose * 100;

    public bool IsUp => Change > 0;

    public bool IsDown => Change < 0;
}
