using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Xml.Linq;
using Xunit;

namespace NetGuardian.App.Tests;

public sealed class ThemeAccessibilityTests
{
    [Fact]
    public void ThemeLabelAutomationNameDoesNotRecurse()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                // 从实际窗口资源提取标签，在独立控件树内验证，不启动网络功能。
                using var stream = typeof(ThemeAccessibilityTests).Assembly.GetManifestResourceStream("MainWindow.xaml")!;
                var document = XDocument.Load(stream);
                XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
                var label = document.Descendants().Single(element => (string?)element.Attribute(xaml + "Name") == "ThemeLabel");
                var panel = (StackPanel)XamlReader.Parse(
                    "<StackPanel xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\">" + label + "</StackPanel>");
                var text = Assert.IsType<TextBlock>(panel.Children[0]);
                text.Text = "主题";
                panel.Measure(new Size(300, 100));
                panel.Arrange(new Rect(0, 0, 300, 100));
                panel.UpdateLayout();

                Assert.Equal("主题", FrameworkElementAutomationPeer.CreatePeerForElement(text)!.GetName());
            }
            catch (Exception exception) { failure = exception; }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "无障碍名称查询未在预期时间内完成。");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
