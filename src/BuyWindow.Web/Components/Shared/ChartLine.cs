namespace BuyWindow.Web.Components.Shared;

/// <summary>An extra line on a chart, one value per point. Null leaves a gap. Slot picks its color, from 2 to 4.</summary>
public sealed record ChartLine(string Name, IReadOnlyList<double?> Values, int Slot);
