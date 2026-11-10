using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NetGuardian.Core.Localization;
using NetGuardian.App.Presentation;
using System.Windows.Threading;
using Wpf.Ui.Controls;
using Xunit;
using Button = System.Windows.Controls.Button;

namespace NetGuardian.App.Tests;

[Collection("WPF 桌面")]
public sealed class DialogPresentationTests
{
    [Theory]
    [InlineData("Dark")]
    [InlineData("Light")]
    [InlineData("HighContrast")]
    public void 表单和确认框在三种主题下共用外壳与禁用语义(string theme)
    {
        DesktopFixture.Run(owner =>
        {
            var mode = Enum.Parse<Wpf.Ui.Appearance.ApplicationTheme>(theme);
            if (mode == Wpf.Ui.Appearance.ApplicationTheme.HighContrast)
                owner.Resources.MergedDictionaries[0] = new ResourceDictionary
                {
                    Source = new Uri("pack://application:,,,/Wpf.Ui;component/Resources/Theme/HCBlack.xaml")
                };
            else owner.Resources.MergedDictionaries[0] = new Wpf.Ui.Markup.ThemesDictionary { Theme = mode };
            owner.Resources.MergedDictionaries.Add(AppThemePolicy.CreatePalette(mode));
            var localizer = new Localizer();
            var confirmation = DialogPresentation.Create(owner, localizer.T("ConfirmDhcpTitle"), localizer.T("ConfirmDhcpMessage"),
                localizer.T("Continue"), localizer.T("Cancel"));
            var form = new AddWifiDialog(localizer, null) { Owner = owner };
            foreach (var dialog in new FluentDialogWindow[] { confirmation, form })
            {
                foreach (var dictionary in owner.Resources.MergedDictionaries) dialog.Resources.MergedDictionaries.Add(dictionary);
                dialog.WindowStartupLocation = WindowStartupLocation.Manual;
                dialog.Left = -20000;
                try
                {
                    dialog.Show();
                    dialog.UpdateLayout();
                    Assert.Equal(((SolidColorBrush)owner.FindResource("SurfaceCardBrush")).Color, ((SolidColorBrush)dialog.Background).Color);
                    var primary = DesktopFixture.Descendants(dialog).OfType<Wpf.Ui.Controls.Button>().Single(button => button.IsDefault);
                    primary.IsEnabled = false;
                    dialog.UpdateLayout();
                    Assert.Equal(((SolidColorBrush)owner.FindResource("DisabledTextBrush")).Color, ((SolidColorBrush)primary.Foreground).Color);
                    var chrome = (Border)primary.Template.FindName("Chrome", primary);
                    Assert.Equal(((SolidColorBrush)owner.FindResource("DisabledControlBrush")).Color, ((SolidColorBrush)chrome.Background).Color);
                    DesktopFixture.Save(dialog, $"{(dialog == form ? "form" : "confirmation")}-{theme}-disabled.png");
                }
                finally { dialog.Close(); }
            }
        });
    }

    [Fact]
    public void 添加无线使用库标题栏并与主窗口共用主按钮颜色()
    {
        DesktopFixture.Run(owner =>
        {
            var dialog = new AddWifiDialog(new Localizer(), null)
            {
                Owner = owner, WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -20000, Top = 0, ShowInTaskbar = false
            };
            foreach (var dictionary in owner.Resources.MergedDictionaries) dialog.Resources.MergedDictionaries.Add(dictionary);
            try
            {
                dialog.Show();
                dialog.UpdateLayout();
                var titleBar = Assert.Single(DesktopFixture.Descendants(dialog).OfType<TitleBar>());
                Assert.IsAssignableFrom<FluentDialogWindow>(dialog);
                Assert.Same(((TitleBar)owner.FindName("TitleBar")).Template, titleBar.Template);
                Assert.False(titleBar.ShowMinimize);
                Assert.False(titleBar.ShowMaximize);
                Assert.True(titleBar.ShowClose);
                var save = DesktopFixture.Descendants(dialog).OfType<Button>().Single(button => button.IsDefault);
                var cancel = DesktopFixture.Descendants(dialog).OfType<Button>().Single(button => button.IsCancel);
                Assert.Equal(ControlAppearance.Secondary, Assert.IsAssignableFrom<Wpf.Ui.Controls.Button>(cancel).Appearance);
                Assert.Equal(((SolidColorBrush)owner.FindResource("PrimaryButtonBrush")).Color, ((SolidColorBrush)save.Background).Color);
                Assert.True(save.TranslatePoint(new Point(), dialog).X < cancel.TranslatePoint(new Point(), dialog).X);
            }
            finally { dialog.Close(); }
        });
    }

    [Theory]
    [InlineData(0, Wpf.Ui.Controls.MessageBoxResult.Primary)]
    [InlineData(1, Wpf.Ui.Controls.MessageBoxResult.Secondary)]
    [InlineData(2, Wpf.Ui.Controls.MessageBoxResult.None)]
    [InlineData(3, Wpf.Ui.Controls.MessageBoxResult.None)]
    public void 统一确认框的模态返回保留三按钮与关闭语义(int action, Wpf.Ui.Controls.MessageBoxResult expected)
    {
        DesktopFixture.Run(owner =>
        {
            var localizer = new Localizer();
            var dialog = DialogPresentation.Create(owner, localizer.T("ClosePromptTitle"), localizer.T("ClosePrompt"),
                localizer.T("CloseTray"), localizer.T("Cancel"), localizer.T("CloseExit"));
            foreach (var dictionary in owner.Resources.MergedDictionaries) dialog.Resources.MergedDictionaries.Add(dictionary);
            Assert.Equal(WindowStartupLocation.CenterOwner, dialog.WindowStartupLocation);
            dialog.WindowStartupLocation = WindowStartupLocation.Manual;
            dialog.Left = -20000;
            Exception? failure = null;
            dialog.Dispatcher.BeginInvoke(() =>
            {
                try
                {
                    Assert.Same(owner, dialog.Owner);
                    var titleBar = Assert.Single(DesktopFixture.Descendants(dialog).OfType<TitleBar>());
                    Assert.Same(((TitleBar)owner.FindName("TitleBar")).Template, titleBar.Template);
                    var buttons = DesktopFixture.Descendants(dialog).OfType<Wpf.Ui.Controls.Button>()
                        .Where(button => button.Style == dialog.FindResource("DialogButtonStyle")).ToArray();
                    Assert.Equal(3, buttons.Length);
                    Assert.True(buttons[0].IsDefault);
                    Assert.True(buttons[2].IsCancel);
                    Assert.Equal(ControlAppearance.Primary, buttons[0].Appearance);
                    Assert.True(buttons[0].TranslatePoint(new Point(), dialog).X < buttons[2].TranslatePoint(new Point(), dialog).X);
                    if (action < 3) buttons[action].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    else dialog.Close();
                }
                catch (Exception exception) { failure = exception; }
                finally { if (dialog.IsVisible) dialog.Close(); }
            }, DispatcherPriority.ContextIdle);
            dialog.ShowDialog();
            if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
            Assert.Equal(expected, dialog.Result);
        });
    }

    [Fact]
    public void 高风险确认保留默认取消且危险按钮使用语义资源()
    {
        DesktopFixture.Run(owner =>
        {
            var localizer = new Localizer();
            var dialog = DialogPresentation.Create(owner, localizer.T("HighRiskConfirmTitle"), localizer.T("HighRiskConfirmText"),
                DialogPresentation.YesButtonText, DialogPresentation.NoButtonText,
                dangerous: true, defaultCancel: true);
            foreach (var dictionary in owner.Resources.MergedDictionaries) dialog.Resources.MergedDictionaries.Add(dictionary);
            dialog.WindowStartupLocation = WindowStartupLocation.Manual;
            dialog.Left = -20000;
            try
            {
                dialog.Show();
                dialog.UpdateLayout();
                var buttons = DesktopFixture.Descendants(dialog).OfType<Wpf.Ui.Controls.Button>()
                    .Where(button => button.Style == dialog.FindResource("DialogButtonStyle")).ToArray();
                var dangerous = buttons.Single(button => button.Appearance == ControlAppearance.Danger);
                Assert.False(dangerous.IsDefault);
                Assert.False(string.IsNullOrWhiteSpace(dangerous.Content?.ToString()));
                Assert.True(buttons.Single(button => button.IsCancel).IsDefault);
                Assert.Equal(((SolidColorBrush)owner.FindResource("DangerButtonBrush")).Color, ((SolidColorBrush)dangerous.Background).Color);
                Assert.Equal(Wpf.Ui.Controls.MessageBoxResult.None, dialog.Result);
            }
            finally { dialog.Close(); }
        });
    }
}
