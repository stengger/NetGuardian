using System.Windows;
using NetGuardian.App.Presentation;
using Xunit;

namespace NetGuardian.App.Tests;

[Collection("WPF 桌面")]
public sealed class FeedbackQueueTests
{
    [Fact]
    public void 警告暂停后继续计时且排队消息显示前不消耗时间()
    {
        var queue = new FeedbackQueue(3, TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(6));
        var warning = queue.Enqueue("警告", "需注意", "Warning", null, null, null);
        queue.Enqueue("错误", "需处理", "Danger", null, null, null);
        var info = queue.Enqueue("信息", "正常", "Info", null, null, null);
        var pending = queue.Enqueue("等待", "稍后出现", "Success", null, null, null);
        warning.Paused = true;
        queue.Advance(TimeSpan.FromSeconds(4));
        Assert.Contains(warning, queue.Visible);
        Assert.DoesNotContain(info, queue.Visible);
        Assert.Contains(pending, queue.Visible);
        Assert.Equal(TimeSpan.FromSeconds(4), pending.Remaining);
        warning.Paused = false;
        queue.Advance(TimeSpan.FromSeconds(5));
        Assert.Contains(warning, queue.Visible);
        queue.Advance(TimeSpan.FromSeconds(1));
        Assert.Single(queue.Visible);
        Assert.Equal("错误", queue.Visible[0].Title);
        queue.Advance(TimeSpan.FromHours(1));
        Assert.Single(queue.Visible);
    }

    [Fact]
    public void 重复提示重置时间并且关闭后下条接替()
    {
        var queue = new FeedbackQueue(1, TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(6));
        var first = queue.Enqueue("保存", "成功", "Success", null, null, null);
        queue.Advance(TimeSpan.FromSeconds(3));
        Assert.Same(first, queue.Enqueue("保存", "成功", "Success", null, null, null));
        queue.Advance(TimeSpan.FromSeconds(2));
        Assert.Equal(2, first.Count);
        var next = queue.Enqueue("错误", "详情", "Danger", null, null, null);
        queue.Dismiss(first);
        Assert.Same(next, Assert.Single(queue.Visible));
    }

    [Fact]
    public void 最多显示三条且相同内容合并而不同撤销目标保持独立()
    {
        DesktopFixture.Run(window =>
        {
            var host = (FeedbackPresenter)window.FindName("RootSnackbar");
            host.ShowMessage("连接", "失败", "Warning");
            host.ShowMessage("连接", "失败", "Warning");
            Assert.Single(host.Children.Cast<UIElement>());
            host.ShowMessage("删除", "相同名称", "Warning", "撤销", () => { }, "网络一");
            host.ShowMessage("删除", "相同名称", "Warning", "撤销", () => { }, "网络二");
            host.ShowMessage("信息", "第四条等待", "Info");
            Assert.Equal(3, host.Children.Count);
        });
    }
}
