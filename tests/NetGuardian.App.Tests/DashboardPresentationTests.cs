using NetGuardian.App.Presentation;
using Xunit;

namespace NetGuardian.App.Tests;

public sealed class DashboardPresentationTests
{
    [Theory]
    [InlineData(0, 0, "—")]
    [InlineData(1, 2, "50%")]
    [InlineData(0, 2, "0%")]
    [InlineData(3, 3, "100%")]
    public void 恢复成功率使用实际尝试且零样本不显示成功(int success, int attempts, string expected) =>
        Assert.Equal(expected, DashboardPresentation.SuccessRate(success, attempts));

    [Theory]
    [InlineData(null, "TextFillColorSecondaryBrush")]
    [InlineData(-1d, "TextFillColorSecondaryBrush")]
    [InlineData(double.NaN, "TextFillColorSecondaryBrush")]
    [InlineData(double.PositiveInfinity, "TextFillColorSecondaryBrush")]
    [InlineData(20d, "StatusHealthyBrush")]
    [InlineData(21d, "StatusWarningBrush")]
    [InlineData(100d, "StatusWarningBrush")]
    [InlineData(101d, "StatusDangerBrush")]
    public void 延迟分级保留无样本并区分边界(double? latency, string expected) =>
        Assert.Equal(expected, DashboardPresentation.LatencyBrushKey(latency, 20, 100));
}
