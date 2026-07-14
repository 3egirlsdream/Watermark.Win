using System.Globalization;

namespace Watermark.Razor.Components.Dashboard;

public enum DashboardChartType
{
    Line,
    Bar,
    Donut,
}

public sealed class DashboardChartSeries
{
    public string Name { get; set; } = string.Empty;
    public string Color { get; set; } = "#2764ff";
    public double[] Values { get; set; } = [];
}

public readonly record struct DashboardDonutSegment(string Name, string Color, double Value, double DashLength, double DashOffset);

public static class DashboardChartMath
{
    public const double ChartLeft = 44;
    public const double ChartTop = 18;
    public const double ChartWidth = 672;
    public const double ChartHeight = 188;
    public const double DonutCircumference = 301.59289474462014;

    public static double Maximum(IEnumerable<DashboardChartSeries> series)
    {
        var maximum = series.SelectMany(x => x.Values ?? []).DefaultIfEmpty(0).Max();
        return maximum <= 0 ? 1 : maximum;
    }

    public static string BuildLinePoints(double[]? values, double maximum)
    {
        values ??= [];
        if (values.Length == 0)
        {
            return string.Empty;
        }

        return string.Join(' ', values.Select((value, index) =>
        {
            var x = values.Length == 1
                ? ChartLeft + ChartWidth / 2
                : ChartLeft + index * (ChartWidth / (values.Length - 1));
            var y = ChartTop + ChartHeight - Math.Max(0, value) / Math.Max(1, maximum) * ChartHeight;
            return $"{x.ToString("0.##", CultureInfo.InvariantCulture)},{y.ToString("0.##", CultureInfo.InvariantCulture)}";
        }));
    }

    public static IReadOnlyList<int> LabelIndexes(int count, int maximumLabels = 6)
    {
        if (count <= 0)
        {
            return [];
        }

        if (count <= maximumLabels)
        {
            return Enumerable.Range(0, count).ToArray();
        }

        return Enumerable.Range(0, maximumLabels)
            .Select(index => (int)Math.Round(index * (count - 1d) / (maximumLabels - 1d)))
            .Distinct()
            .ToArray();
    }

    public static IReadOnlyList<DashboardDonutSegment> BuildDonutSegments(IEnumerable<DashboardChartSeries> series)
    {
        var values = series
            .Select(x => new { x.Name, x.Color, Value = Math.Max(0, x.Values?.FirstOrDefault() ?? 0) })
            .Where(x => x.Value > 0)
            .ToList();
        var total = values.Sum(x => x.Value);
        if (total <= 0)
        {
            return [];
        }

        var offset = 0d;
        var result = new List<DashboardDonutSegment>();
        foreach (var value in values)
        {
            var length = value.Value / total * DonutCircumference;
            result.Add(new DashboardDonutSegment(value.Name, value.Color, value.Value, length, -offset));
            offset += length;
        }

        return result;
    }
}
