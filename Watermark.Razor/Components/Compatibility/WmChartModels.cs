namespace Watermark.Razor.Components.Compatibility;

public sealed class WmChartSeries
{
    public string Name { get; set; } = string.Empty;
    public double[] Data { get; set; } = [];
}

public sealed class WmChartOptions
{
    public int YAxisTicks { get; set; }
    public int MaxNumYAxisTicks { get; set; }
}
