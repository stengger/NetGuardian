using NetGuardian.App.Presentation;
using NetGuardian.Core.Models;
using Xunit;

namespace NetGuardian.App.Tests;

[Collection("WPF 桌面")]
public class DashboardSampleTests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.Parse("2026-11-22T10:00:00+08:00");
    private static ConnectivityReport Sample(int minutes, string? network, bool light, bool dnsOk = true) => new()
    {
        CheckedAt = Start.AddMinutes(minutes), NetworkContext = network, IsLightweight = light,
        State = NetworkState.Healthy, Probes = light ? [new("HTTPS test", true, 18)] :
            [new("Gateway ping", true, 2), new("DNS test", dnsOk, dnsOk ? 12 : null), new("HTTPS test", true, 25)]
    };

    [Fact]
    public void 轻量报告保留同网样本和来源且不混入原始探针()
    {
        var selection = new ConnectivityReportSelection();
        var full = Sample(0, "A", false);
        var light = Sample(1, "A", true);
        selection.TryAccept(full, ConnectivityReportOrigin.Manual);
        selection.TryAccept(light, ConnectivityReportOrigin.Background);
        var dns = selection.Measurement("Dns")!;
        Assert.Same(full, dns.Report);
        Assert.Equal(ConnectivityReportOrigin.Manual, dns.Origin);
        Assert.Equal(12d, dns.Timing.AverageMs);
        Assert.Same(light, selection.Measurement("Https")!.Report);
        Assert.Single(selection.Report!.Probes);
        Assert.False(dns.Expired(Start.AddMinutes(4), 90));
        Assert.True(dns.Expired(Start.AddMinutes(6), 90));
    }

    [Theory]
    [InlineData("B")]
    [InlineData(null)]
    public void 切换网络或身份未知时清除此前样本(string? destination)
    {
        var selection = new ConnectivityReportSelection();
        selection.TryAccept(Sample(0, "A", false), ConnectivityReportOrigin.Foreground);
        selection.TryAccept(Sample(1, destination, true), ConnectivityReportOrigin.Background);
        Assert.Null(selection.Measurement("Gateway"));
        Assert.Null(selection.Measurement("Dns"));
    }

    [Fact]
    public void 本次失败覆盖显示且可查上次成功并拒绝更早心跳()
    {
        var selection = new ConnectivityReportSelection();
        selection.TryAccept(Sample(0, "A", false), ConnectivityReportOrigin.Background);
        var fail = Sample(2, "A", false, false);
        selection.TryAccept(fail, ConnectivityReportOrigin.Manual);
        Assert.False(selection.TryAccept(Sample(1, "A", false), ConnectivityReportOrigin.Background));
        Assert.Same(fail, selection.Measurement("Dns")!.Report);
        Assert.Null(selection.Measurement("Dns")!.Timing.AverageMs);
        Assert.Equal(12d, selection.Measurement("Dns")!.LastValidMs);
    }
    [Fact]
    public void 网络变化后迟到的报告不能恢复旧读数()
    {
        var selection = new ConnectivityReportSelection();
        selection.TryAccept(Sample(0, "A", false), ConnectivityReportOrigin.Manual);
        selection.NetworkChanged(Start.AddMinutes(1));
        Assert.False(selection.TryAccept(new() { SampleStartedAt = Start, CheckedAt = Start.AddMinutes(2) }, ConnectivityReportOrigin.Background));
        Assert.Null(selection.Measurement("Dns"));
    }

    [Fact]
    public void 诊断卡片呈现实测时间跳过过期和失败()
    {
        DesktopFixture.Run(window =>
        {
            var value = (System.Windows.Controls.TextBlock)window.FindName("HomeDnsValue");
            var caption = (System.Windows.Controls.TextBlock)window.FindName("HomeDnsSamples");
            var selection = new ConnectivityReportSelection();
            var text = new NetGuardian.Core.Localization.Localizer();
            void Render(int minute) => DashboardPresentation.RenderMeasurement(value, caption, selection.Measurement("Dns"),
                "Dns", text, Start.AddMinutes(minute), 90, true);
            Render(0);
            Assert.Contains("等待", caption.Text);
            selection.TryAccept(Sample(0, "A", false), ConnectivityReportOrigin.Manual);
            selection.TryAccept(Sample(1, "A", true), ConnectivityReportOrigin.Foreground);
            Render(1);
            Assert.Contains("12 ms", value.Text);
            Assert.Contains("跳过", caption.Text);
            Render(31);
            Assert.Contains("过期", caption.Text);
            selection.TryAccept(Sample(32, "A", false, false), ConnectivityReportOrigin.Foreground);
            Render(32);
            Assert.Contains("失败", value.Text);
            Assert.Contains("12 ms", caption.Text);
        });
    }
}
