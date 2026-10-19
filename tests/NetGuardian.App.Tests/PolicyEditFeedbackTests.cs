using System.Windows.Controls;
using NetGuardian.App.Presentation;
using NetGuardian.Core.Localization;
using NetGuardian.Core.Settings;
using NetGuardian.Core.Models;
using System.IO;
using Xunit;

namespace NetGuardian.App.Tests;

[Collection("WPF 桌面")]
public sealed class PolicyEditFeedbackTests
{
    [Fact]
    public void 保存失败不能把未保存策略留给正在运行的监控()
    {
        var settings = new AppSettings();
        var previous = settings.Policy;
        var edited = new MonitorPolicy { CheckIntervalSeconds = 120 };
        Assert.Throws<IOException>(() => PolicySaveTransaction.Commit(settings, edited, _ => throw new IOException("磁盘不可写")));
        Assert.Same(previous, settings.Policy);
        Assert.Equal(30, settings.Policy.CheckIntervalSeconds);
    }

    [Fact]
    public void 磁盘写入期间引擎仍应读取原有策略()
    {
        var settings = new AppSettings();
        var original = settings.Policy;
        var candidate = new MonitorPolicy { CheckIntervalSeconds = 120 };
        PolicySaveTransaction.Commit(settings, candidate, snapshot =>
        {
            Assert.Same(original, settings.Policy);
            Assert.Equal(120, snapshot.Policy.CheckIntervalSeconds);
        });
        Assert.Equal(120, settings.Policy.CheckIntervalSeconds);
    }

    [Fact]
    public void 修改表单后显示未保存而加载规范化数值不产生假修改()
    {
        DesktopFixture.Run(window =>
        {
            var panel = (Border)window.FindName("PolicyPanel");
            var status = (TextBlock)window.FindName("PolicyStatusText");
            var field = (TextBox)window.FindName("DnsProbeHostsTextBox");
            var feedback = new PolicyEditFeedback(panel, status, new Localizer(), () => field.Text);
            using (feedback.SuppressChanges()) field.Text = "www.microsoft.com";
            Assert.False(feedback.IsDirty);
            field.Text = "www.baidu.com";
            Assert.True(feedback.IsDirty);
            Assert.Contains("未保存", status.Text);
            feedback.MarkSaved("已保存：30秒");
            Assert.False(feedback.IsDirty);
            Assert.Equal("已保存：30秒", status.Text);
        });
    }

    [Fact]
    public void 编辑后恢复原值以及拒绝开关修改都不应误报未保存()
    {
        DesktopFixture.Run(window =>
        {
            var panel = (Border)window.FindName("PolicyPanel");
            var status = (TextBlock)window.FindName("PolicyStatusText");
            var toggle = (Wpf.Ui.Controls.ToggleSwitch)window.FindName("HighRiskCheckBox");
            var feedback = new PolicyEditFeedback(panel, status, new Localizer(), () => toggle.IsChecked.ToString()!);
            toggle.IsChecked = true;
            Assert.True(feedback.IsDirty);
            // 模拟确认框选“否”后恢复原值，不需要真实弹出对话框。
            toggle.IsChecked = false;
            Assert.False(feedback.IsDirty);
            Assert.DoesNotContain("未保存的修改", status.Text);
        });
    }

    [Fact]
    public void 数值提交后正确标记修改并允许恢复原值()
    {
        DesktopFixture.Run(window =>
        {
            var panel = (Border)window.FindName("PolicyPanel");
            var status = (TextBlock)window.FindName("PolicyStatusText");
            var field = (Wpf.Ui.Controls.NumberBox)window.FindName("IntervalTextBox");
            var feedback = new PolicyEditFeedback(panel, status, new Localizer(), () => field.Value.ToString()!);
            field.Value = 60;
            Assert.True(feedback.IsDirty);
            field.Value = 30;
            Assert.False(feedback.IsDirty);
        });
    }
}
