using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using NetGuardian.App.Presentation;
using Xunit;
namespace NetGuardian.App.Tests;
[Collection("WPF 桌面")]
public sealed class UiConsistencyTests
{
    [Fact]
    public void 更新说明和单位字段复用展开样式与已有标签()
    {
        DesktopFixture.Run(window =>
        {
            Assert.Same(window.FindResource("ContentExpanderStyle"), ((Expander)window.FindName("UpdateNotesExpander")).Style);
            foreach (var (name, label) in new[] { ("IntervalUnitComboBox", "IntervalLabel"), ("CooldownUnitComboBox", "CooldownLabel") })
                Assert.Same(window.FindName(label), AutomationProperties.GetLabeledBy((DependencyObject)window.FindName(name)));
            var host = (FeedbackPresenter)window.FindName("RootSnackbar");
            host.ShowMessage("错误", "错误原文", "Error");
            var card = DesktopFixture.Descendants(host).OfType<Border>().Single(b => b.Name == "FeedbackCard");
            Assert.Equal(AutomationLiveSetting.Assertive, AutomationProperties.GetLiveSetting(card));
        });
    }
}
