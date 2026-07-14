using Watermark.Shared.Models;
using Watermark.Razor.Components.Dashboard;
using Xunit;

namespace Watermark.Razor.Tests;

public class AdminDashboardSharedTests
{
    [Theory]
    [InlineData("0BECCA9A-6F10-4A88-8753-921195D08853")]
    [InlineData("9debf7dc-f58c-4667-bacf-a6bfd18352eb")]
    public void AdminAccessPolicy_accepts_configured_ids(string id)
    {
        Assert.True(AdminAccessPolicy.IsAdmin(new WMLoginChildModel { ID = id, USER_NAME = "normal" }));
    }

    [Theory]
    [InlineData("cxk")]
    [InlineData("CXK")]
    public void AdminAccessPolicy_accepts_legacy_username(string username)
    {
        Assert.True(AdminAccessPolicy.IsAdmin(new WMLoginChildModel { ID = "other", USER_NAME = username }));
    }

    [Fact]
    public void AdminAccessPolicy_rejects_missing_and_regular_users()
    {
        Assert.False(AdminAccessPolicy.IsAdmin(null));
        Assert.False(AdminAccessPolicy.IsAdmin(new WMLoginChildModel()));
        Assert.False(AdminAccessPolicy.IsAdmin(new WMLoginChildModel { ID = "other", USER_NAME = "member" }));
    }

    [Fact]
    public void Dashboard_path_uses_invariant_date_format()
    {
        var result = APIHelper.BuildDashboardOverviewPath(
            new DateTime(2026, 1, 2, 23, 59, 0),
            new DateTime(2026, 7, 13, 8, 30, 0));

        Assert.Equal("/api/Dashboard/GetOverview?startDate=2026-01-02&endDate=2026-07-13", result);
    }

    [Fact]
    public void Dashboard_chart_handles_zero_and_single_point_series()
    {
        var zeroMaximum = DashboardChartMath.Maximum([
            new DashboardChartSeries { Values = [0, 0, 0] },
        ]);
        var singlePoint = DashboardChartMath.BuildLinePoints([5], 5);

        Assert.Equal(1, zeroMaximum);
        Assert.Equal("380,18", singlePoint);
    }

    [Fact]
    public void Dashboard_chart_limits_long_axis_labels_and_preserves_ends()
    {
        var indexes = DashboardChartMath.LabelIndexes(366);

        Assert.Equal(6, indexes.Count);
        Assert.Equal(0, indexes[0]);
        Assert.Equal(365, indexes[^1]);
    }

    [Fact]
    public void Dashboard_chart_builds_proportional_donut_segments()
    {
        var segments = DashboardChartMath.BuildDonutSegments([
            new DashboardChartSeries { Name = "Mac", Values = [75], Color = "#1" },
            new DashboardChartSeries { Name = "Windows", Values = [25], Color = "#2" },
            new DashboardChartSeries { Name = "空值", Values = [0], Color = "#3" },
        ]);

        Assert.Equal(2, segments.Count);
        Assert.Equal(DashboardChartMath.DonutCircumference * 0.75, segments[0].DashLength, 6);
        Assert.Equal(DashboardChartMath.DonutCircumference * 0.25, segments[1].DashLength, 6);
    }
}
