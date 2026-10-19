using NetGuardian.App.Presentation;
using Xunit;

namespace NetGuardian.App.Tests;

public sealed class PublicIpPresentationTests
{
    [Theory]
    [InlineData(null, null)]
    [InlineData(0d, null)]
    [InlineData(double.NaN, 0d)]
    [InlineData(91d, 0d)]
    [InlineData(0d, 181d)]
    public void 缺失或非法坐标显示未知(double? latitude, double? longitude)
        => Assert.Equal("未知", PublicIpPresentation.Coordinates(latitude, longitude, "未知"));

    [Fact]
    public void 零坐标仍是有效坐标且顺序为纬度经度()
    {
        Assert.StartsWith("0.0000", PublicIpPresentation.Coordinates(0, 0, "未知"));
        Assert.StartsWith("30.0000", PublicIpPresentation.Coordinates(30, 120, "未知"));
        Assert.EndsWith("120.0000", PublicIpPresentation.Coordinates(30, 120, "未知"));
    }
}
