namespace BuyWindow.Web.Components.Shared;

public sealed record ChartPoint(string Label, double Value, string Tip, double? Low = null, double? High = null, bool Marked = false, double? Second = null);
